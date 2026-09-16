//go:build windows

package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime/debug"
	"strconv"
	"strings"
	"syscall"
	"time"

	"github.com/lxn/walk"
	. "github.com/lxn/walk/declarative"
)

const (
	releaseChromeURL = "https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/chrome_elf.dll"
	releaseBlockURL  = "https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/blockthespot.dll"

	installerLatestReleaseAPI = "https://api.github.com/repos/Nuzair46/BlockTheSpot-Installer/releases/latest"
	installerReleasesURL      = "https://github.com/Nuzair46/BlockTheSpot-Installer/releases/latest"
)

var installerVersion = "dev"

type installOptions struct {
	UpdateSpotify       bool
	LaunchSpotifyOnDone bool
	SpotifyVersion      spotifyInstallChoice
}

type operationMode int

const (
	operationInstall operationMode = iota
	operationUninstall
)

type installer struct {
	options          installOptions
	minimumVersion   string
	downloadedConfig []byte
	logf             func(format string, args ...any)
	setProgress      func(value int)
	setStatus        func(status string)
}

type githubRelease struct {
	TagName string `json:"tag_name"`
	HTMLURL string `json:"html_url"`
}

type installerApp struct {
	mw              *walk.MainWindow
	logoView        *walk.ImageView
	updateInfo      *walk.LinkLabel
	updateCheck     *walk.CheckBox
	versionCombo    *walk.ComboBox
	refreshButton   *walk.PushButton
	launchCheck     *walk.CheckBox
	progress        *walk.ProgressBar
	status          *walk.Label
	logView         *walk.TextEdit
	installButton   *walk.PushButton
	uninstallButton *walk.PushButton
	spotifyVersions []spotifyInstallChoice
	busy            bool
	loadingVersions bool
}

func main() {
	defer func() {
		if r := recover(); r != nil {
			details := fmt.Sprintf("Unhandled panic: %v\r\n\r\n%s", r, string(debug.Stack()))
			reportFatalError(details)
			os.Exit(1)
		}
	}()

	app := &installerApp{}
	if err := app.run(); err != nil {
		reportFatalError(err.Error())
		os.Exit(1)
	}
}

func (a *installerApp) run() error {
	appIcon, _ := loadAppIcon()

	if err := (MainWindow{
		AssignTo: &a.mw,
		Title:    "BlockTheSpot Installer",
		Icon:     appIcon,
		Size:     Size{Width: 760, Height: 560},
		MinSize:  Size{Width: 760, Height: 560},
		Layout:   VBox{},
		Children: []Widget{
			Composite{
				Layout: HBox{},
				Children: []Widget{
					ImageView{
						AssignTo: &a.logoView,
						MinSize:  Size{Width: 56, Height: 56},
						MaxSize:  Size{Width: 56, Height: 56},
						Mode:     ImageViewModeZoom,
					},
					Composite{
						Layout: VBox{},
						Children: []Widget{
							TextLabel{Text: "BlockTheSpot Installer"},
							TextLabel{Text: "Windows x64 Spotify patch installer"},
						},
					},
				},
			},
			LinkLabel{
				AssignTo: &a.updateInfo,
				Text:     "Installer version: checking for updates...",
				OnLinkActivated: func(link *walk.LinkLabelLink) {
					_ = openExternalURL(link.URL())
				},
			},
			Composite{
				Layout: VBox{},
				Children: []Widget{
					TextLabel{Text: "Spotify version to install"},
					Composite{
						Layout: HBox{},
						Children: []Widget{
							ComboBox{
								AssignTo:      &a.versionCombo,
								Editable:      false,
								Model:         []string{"Loading available Windows x64 versions..."},
								StretchFactor: 1,
							},
							PushButton{AssignTo: &a.refreshButton, Text: "Refresh versions", OnClicked: a.refreshSpotifyVersions},
						},
					},
				},
			},
			CheckBox{AssignTo: &a.updateCheck, Text: "Update or reinstall Spotify before patching", Checked: false},
			CheckBox{AssignTo: &a.launchCheck, Text: "Launch Spotify and close installer after completion", Checked: true},
			ProgressBar{AssignTo: &a.progress, MinValue: 0, MaxValue: 100},
			Label{AssignTo: &a.status, Text: "Idle"},
			TextEdit{AssignTo: &a.logView, ReadOnly: true, VScroll: true},
			LinkLabel{
				Text: `Credits: <a id="bts" href="https://github.com/Nuzair46/BlockTheSpot">BlockTheSpot (Nuzair46)</a> | <a id="installer" href="https://github.com/Nuzair46/BlockTheSpot-Installer">BlockTheSpot Installer (Nuzair46)</a> | <a id="discord" href="https://discord.gg/eYudMwgYtY">Discord Server</a>`,
				OnLinkActivated: func(link *walk.LinkLabelLink) {
					_ = openExternalURL(link.URL())
				},
			},
			Composite{
				Layout: HBox{Alignment: AlignHFarVCenter},
				Children: []Widget{
					PushButton{
						AssignTo: &a.installButton,
						Text:     "Install / Patch",
						OnClicked: func() {
							a.startInstall()
						},
					},
					PushButton{
						AssignTo: &a.uninstallButton,
						Text:     "Uninstall / Restore",
						OnClicked: func() {
							a.startUninstall()
						},
					},
					PushButton{Text: "Exit", OnClicked: func() { a.mw.Close() }},
				},
			},
		},
	}).Create(); err != nil {
		return err
	}

	if appIcon != nil && a.logoView != nil {
		_ = a.logoView.SetImage(appIcon)
	}
	if a.versionCombo != nil {
		a.versionCombo.SetEnabled(false)
	}
	a.setUpdateInfo(fmt.Sprintf("Installer version: %s", installerVersion))
	go a.checkForInstallerUpdate()
	a.refreshSpotifyVersions()

	a.mw.Run()
	return nil
}

func reportFatalError(details string) {
	logPath, err := writeStartupErrorLog(details)
	msg := details
	if err == nil {
		msg += "\r\n\r\nLog file:\r\n" + logPath
	}

	fmt.Fprintln(os.Stderr, details)
	_ = walk.MsgBox(nil, "BlockTheSpot Installer Error", msg, walk.MsgBoxOK|walk.MsgBoxIconError)
}

func writeStartupErrorLog(details string) (string, error) {
	dir := filepath.Join(os.TempDir(), "BlockTheSpotInstaller")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return "", err
	}

	filename := fmt.Sprintf("startup-error-%s.log", time.Now().Format("20060102-150405"))
	path := filepath.Join(dir, filename)
	content := fmt.Sprintf("[%s] %s\r\n", time.Now().Format(time.RFC3339), details)
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		return "", err
	}
	return path, nil
}

func (a *installerApp) startInstall() {
	a.startOperation(operationInstall)
}

func (a *installerApp) startUninstall() {
	a.startOperation(operationUninstall)
}

func (a *installerApp) startOperation(mode operationMode) {
	if a.busy || (mode == operationInstall && a.loadingVersions) {
		return
	}
	opts := installOptions{
		UpdateSpotify:       a.updateCheck.Checked(),
		LaunchSpotifyOnDone: a.launchCheck.Checked(),
		SpotifyVersion:      a.selectedSpotifyVersion(),
	}

	a.setBusy(true)
	a.setProgressSafe(0)
	a.setStatusSafe("Starting")
	if mode == operationUninstall {
		a.logfSafe("Starting uninstaller.")
	} else {
		a.logfSafe("Starting installer.")
	}

	go func() {
		ins := installer{
			options:     opts,
			logf:        a.logfSafe,
			setProgress: a.setProgressSafe,
			setStatus:   a.setStatusSafe,
		}

		var err error
		if mode == operationUninstall {
			err = ins.runUninstall()
		} else {
			err = ins.runInstall()
		}

		a.mw.Synchronize(func() {
			a.setBusy(false)
			if err != nil {
				a.setStatusSafe("Failed")
				walk.MsgBox(a.mw, "Installer Error", err.Error(), walk.MsgBoxIconError)
				return
			}

			a.progress.SetValue(100)
			a.status.SetText("Completed")
			if opts.LaunchSpotifyOnDone {
				a.mw.Close()
			}
		})
	}()
}

func (a *installerApp) setBusy(busy bool) {
	a.busy = busy
	a.installButton.SetEnabled(!busy && !a.loadingVersions)
	a.uninstallButton.SetEnabled(!busy)
	a.updateCheck.SetEnabled(!busy)
	if a.versionCombo != nil {
		a.versionCombo.SetEnabled(!busy && !a.loadingVersions && len(a.spotifyVersions) > 0)
	}
	a.refreshButton.SetEnabled(!busy && !a.loadingVersions)
	a.launchCheck.SetEnabled(!busy)
}

func (a *installerApp) logfSafe(format string, args ...any) {
	msg := fmt.Sprintf(format, args...)
	line := fmt.Sprintf("[%s] %s", time.Now().Format("15:04:05"), msg)
	a.mw.Synchronize(func() {
		current := a.logView.Text()
		if current == "" {
			a.logView.SetText(line)
		} else {
			a.logView.SetText(current + "\r\n" + line)
		}
		caret := len(a.logView.Text())
		a.logView.SetTextSelection(caret, caret)
	})
}

func (a *installerApp) setProgressSafe(value int) {
	if value < 0 {
		value = 0
	}
	if value > 100 {
		value = 100
	}
	a.mw.Synchronize(func() {
		a.progress.SetValue(value)
	})
}

func (a *installerApp) setStatusSafe(status string) {
	a.mw.Synchronize(func() {
		a.status.SetText(status)
	})
}

func (a *installerApp) setUpdateInfo(text string) {
	if a.updateInfo == nil {
		return
	}

	a.mw.Synchronize(func() {
		_ = a.updateInfo.SetText(text)
	})
}

func (a *installerApp) refreshSpotifyVersions() {
	if a.busy || a.loadingVersions {
		return
	}
	previous := a.selectedSpotifyVersion()
	a.loadingVersions = true
	a.setBusy(false)
	go a.loadSpotifyVersionChoices(previous)
}

func (a *installerApp) loadSpotifyVersionChoices(previous spotifyInstallChoice) {
	minimum, choices, selectedIndex, err := fetchSpotifyInstallChoices()
	if err != nil {
		a.logfSafe("Warning: failed to load Spotify version list: %v", err)
	}
	if minimum != "" {
		a.logfSafe("Minimum supported Spotify version: %s. Loaded %d pinned releases from LoaderSpot.", minimum, len(choices)-1)
	}

	a.mw.Synchronize(func() {
		a.spotifyVersions = choices
		model := make([]string, 0, len(choices))
		for _, choice := range choices {
			model = append(model, choice.Display)
		}

		if a.versionCombo != nil {
			for index, choice := range choices {
				if previous.URL != "" && choice.URL == previous.URL {
					selectedIndex = index
					break
				}
			}
			_ = a.versionCombo.SetModel(model)
			if selectedIndex < 0 || selectedIndex >= len(model) {
				selectedIndex = 0
			}
			_ = a.versionCombo.SetCurrentIndex(selectedIndex)
		}
		a.loadingVersions = false
		a.setBusy(a.busy)
	})
}

func (a *installerApp) selectedSpotifyVersion() spotifyInstallChoice {
	if a.versionCombo == nil {
		return spotifyInstallChoice{}
	}

	index := a.versionCombo.CurrentIndex()
	if index < 0 || index >= len(a.spotifyVersions) {
		return spotifyInstallChoice{}
	}

	return a.spotifyVersions[index]
}

func (a *installerApp) checkForInstallerUpdate() {
	release, err := fetchLatestInstallerRelease()
	if err != nil {
		a.setUpdateInfo(fmt.Sprintf("Installer version: %s (update check unavailable)", installerVersion))
		return
	}

	cmp, err := compareInstallerVersion(installerVersion, release.TagName)
	if err != nil {
		a.setUpdateInfo(fmt.Sprintf("Installer version: %s (latest: %s)", installerVersion, release.TagName))
		return
	}

	if cmp < 0 {
		a.setUpdateInfo(fmt.Sprintf(`Update available: <a href="%s">%s</a> (current %s)`, release.HTMLURL, release.TagName, installerVersion))
		return
	}

	a.setUpdateInfo(fmt.Sprintf("Installer is up to date (%s)", installerVersion))
}

func (i *installer) runInstall() error {
	spotifyDir := defaultSpotifyDir()
	if spotifyDir == "" {
		return errors.New("unable to determine Spotify directory")
	}

	i.setStatus("Stopping Spotify")
	i.setProgress(5)
	i.logf("Stopping Spotify processes.")
	stopSpotifyProcesses()

	i.setStatus("Checking Store edition")
	i.setProgress(12)
	storeInstalled, err := isSpotifyStoreInstalled()
	if err != nil {
		i.logf("Warning: failed to check Microsoft Store Spotify: %v", err)
	} else if storeInstalled {
		i.logf("Microsoft Store Spotify detected.")
		i.logf("Uninstalling Microsoft Store Spotify.")
		if err := uninstallSpotifyStore(); err != nil {
			return fmt.Errorf("failed to uninstall Microsoft Store Spotify: %w", err)
		}
		i.logf("Microsoft Store Spotify removed.")
	} else {
		i.logf("Microsoft Store Spotify not detected.")
	}

	i.setStatus("Loading config")
	i.setProgress(15)
	if err := i.loadConfig(); err != nil {
		return err
	}
	i.logf("Minimum supported Spotify version from config.ini: %s", i.minimumVersion)

	spotifyExe := filepath.Join(spotifyDir, "Spotify.exe")
	spotifyInstalled := fileExists(spotifyExe)
	selectedVersion := i.options.SpotifyVersion
	selectedBaseVersion := selectedVersion.BaseVersion
	if selectedBaseVersion == "" && selectedVersion.FullVersion != "" {
		selectedBaseVersion = baseSpotifyVersion(selectedVersion.FullVersion)
	}

	detectedVersion := ""
	unsupportedVersion := false
	selectedVersionMismatch := false
	if spotifyInstalled {
		v, err := getSpotifyVersion(spotifyExe)
		if err != nil {
			i.logf("Warning: unable to read Spotify version: %v", err)
		} else {
			detectedVersion = v
			i.logf("Detected Spotify version: %s", v)
			unsupportedVersion = compareVersion(v, i.minimumVersion) < 0
			if selectedBaseVersion != "" && !strings.EqualFold(baseSpotifyVersion(v), selectedBaseVersion) {
				selectedVersionMismatch = true
				i.logf("Installed Spotify version %s does not match selected version %s; reinstall will be forced.", v, selectedVersion.FullVersion)
			}
		}
	}

	if unsupportedVersion {
		i.logf(
			"Spotify version %s is below supported minimum %s",
			detectedVersion,
			i.minimumVersion,
		)
	}

	if unsupportedVersion && !i.options.UpdateSpotify && !selectedVersionMismatch {
		return fmt.Errorf(
			"Spotify version %s is below supported minimum %s. Enable 'Update or reinstall Spotify before patching' and run again",
			detectedVersion,
			i.minimumVersion,
		)
	}

	needsInstall := !spotifyInstalled || i.options.UpdateSpotify || selectedVersionMismatch
	if needsInstall {
		if selectedVersion.FullVersion != "" {
			i.logf("Selected Spotify version for install: %s", selectedVersion.FullVersion)
		} else {
			i.logf("Spotify version list unavailable; using latest official Spotify x64 installer.")
		}

		if selectedVersion.FullVersion != "" && compareVersion(selectedVersion.FullVersion, i.minimumVersion) < 0 {
			return fmt.Errorf(
				"selected Spotify version %s is below the recommended supported version %s",
				selectedVersion.FullVersion,
				i.minimumVersion,
			)
		}

		i.setStatus("Installing Spotify")
		i.setProgress(20)
		if err := os.MkdirAll(spotifyDir, 0o755); err != nil {
			return fmt.Errorf("failed to prepare Spotify directory: %w", err)
		}

		if err := i.installSpotify(spotifyExe, selectedVersion); err != nil {
			return err
		}
	} else {
		i.logf("Spotify update not required.")
		i.setProgress(45)
	}

	installedVersion, err := getSpotifyVersion(spotifyExe)
	if err != nil {
		return fmt.Errorf("failed to verify Spotify before patching: %w", err)
	}
	if err := validateInstalledSpotifyVersion(installedVersion, i.minimumVersion, selectedVersion); err != nil {
		return err
	}
	i.logf("Verified installed Spotify version: %s.", installedVersion)

	i.setStatus("Applying BlockTheSpot files")
	if err := i.patchSpotify(spotifyDir); err != nil {
		return err
	}

	if i.options.LaunchSpotifyOnDone {
		i.setStatus("Launching Spotify")
		i.logf("Starting Spotify.")
		if err := startSpotify(spotifyExe, spotifyDir); err != nil {
			return fmt.Errorf("failed to launch Spotify: %w", err)
		}
		time.Sleep(2 * time.Second)
		running, err := processRunning("Spotify.exe")
		if err == nil && !running {
			return errors.New("Spotify did not stay running after patch. Re-run install and ensure Spotify can start normally")
		}
	}

	i.setStatus("Completed")
	i.logf("Install finished successfully.")
	i.setProgress(100)
	return nil
}

func (i *installer) runUninstall() error {
	spotifyDir := defaultSpotifyDir()
	if spotifyDir == "" {
		return errors.New("unable to determine Spotify directory")
	}

	spotifyExe := filepath.Join(spotifyDir, "Spotify.exe")
	requiredPath := filepath.Join(spotifyDir, "chrome_elf_required.dll")
	chromePath := filepath.Join(spotifyDir, "chrome_elf.dll")
	blockPath := filepath.Join(spotifyDir, "blockthespot.dll")
	configPath := filepath.Join(spotifyDir, "config.ini")

	i.setStatus("Stopping Spotify")
	i.setProgress(5)
	i.logf("Stopping Spotify processes.")
	stopSpotifyProcesses()

	i.setStatus("Removing BlockTheSpot files")
	i.setProgress(25)
	if fileExists(blockPath) {
		if err := os.Remove(blockPath); err != nil {
			return fmt.Errorf("failed to delete blockthespot.dll: %w", err)
		}
		i.logf("Removed blockthespot.dll.")
	} else {
		i.logf("blockthespot.dll not found.")
	}

	if fileExists(configPath) {
		if err := os.Remove(configPath); err != nil {
			return fmt.Errorf("failed to delete config.ini: %w", err)
		}
		i.logf("Removed config.ini.")
	} else {
		i.logf("config.ini not found.")
	}

	i.setStatus("Restoring chrome_elf.dll")
	i.setProgress(60)
	if fileExists(requiredPath) {
		if fileExists(chromePath) {
			if err := os.Remove(chromePath); err != nil {
				return fmt.Errorf("failed to delete patched chrome_elf.dll: %w", err)
			}
			i.logf("Removed patched chrome_elf.dll.")
		}

		if err := os.Rename(requiredPath, chromePath); err != nil {
			return fmt.Errorf("failed to restore original chrome_elf.dll: %w", err)
		}
		i.logf("Restored original chrome_elf.dll.")
	} else {
		i.logf("chrome_elf_required.dll backup not found; restore skipped.")
	}

	if i.options.LaunchSpotifyOnDone {
		i.setStatus("Launching Spotify")
		i.setProgress(90)
		if fileExists(spotifyExe) {
			i.logf("Starting Spotify.")
			if err := startSpotify(spotifyExe, spotifyDir); err != nil {
				return fmt.Errorf("failed to launch Spotify: %w", err)
			}
		} else {
			i.logf("Spotify.exe not found; skipping launch.")
		}
	}

	i.setStatus("Completed")
	i.setProgress(100)
	i.logf("Uninstall/restore finished successfully.")
	return nil
}

func (i *installer) installSpotify(spotifyExe string, selectedVersion spotifyInstallChoice) error {
	tempDir, err := os.MkdirTemp("", "blockthespot-spotify-setup-")
	if err != nil {
		return fmt.Errorf("failed to create temp directory: %w", err)
	}
	defer os.RemoveAll(tempDir)

	setupPath := filepath.Join(tempDir, "SpotifyFullSetupX64.exe")
	if err := downloadSpotifyInstaller(selectedVersion, setupPath, i.logf); err != nil {
		return err
	}

	i.setProgress(35)
	i.logf("Running Spotify installer.")
	if isRunningAsAdmin() {
		i.logf("Installer is running as administrator, launching setup via a temporary scheduled task.")
		if err := runInstallerViaScheduledTask(setupPath); err != nil {
			i.logf("Warning: scheduled task launch failed: %v", err)
			i.logf("Falling back to direct installer launch.")
			if err := launchDetached(setupPath); err != nil {
				return fmt.Errorf("failed to launch Spotify installer: %w", err)
			}
		}
	} else {
		if err := launchDetached(setupPath); err != nil {
			return fmt.Errorf("failed to launch Spotify installer: %w", err)
		}
	}

	i.logf("Waiting for Spotify install/update to finish.")
	if err := waitForFile(spotifyExe, 6*time.Minute); err != nil {
		return err
	}
	_ = waitForProcess("Spotify.exe", 90*time.Second)

	i.logf("Stopping Spotify after install/update.")
	stopSpotifyProcesses()
	i.setProgress(45)
	return nil
}

func (i *installer) patchSpotify(spotifyDir string) error {
	requiredPath := filepath.Join(spotifyDir, "chrome_elf_required.dll")
	chromePath := filepath.Join(spotifyDir, "chrome_elf.dll")
	blockPath := filepath.Join(spotifyDir, "blockthespot.dll")
	configPath := filepath.Join(spotifyDir, "config.ini")

	if err := removeIfExists(blockPath); err != nil {
		return fmt.Errorf("failed to delete blockthespot.dll: %w", err)
	}

	switch {
	case fileExists(requiredPath):
		i.logf("Preserving existing chrome_elf_required.dll backup.")
	case fileExists(chromePath):
		if err := os.Rename(chromePath, requiredPath); err != nil {
			return fmt.Errorf("failed to rename chrome_elf.dll to chrome_elf_required.dll: %w", err)
		}
		i.logf("Backed up original chrome_elf.dll to chrome_elf_required.dll.")
	default:
		i.logf("Warning: chrome_elf.dll was not found before patching.")
	}

	i.setProgress(60)
	i.logf("Downloading latest chrome_elf.dll.")
	if err := downloadFile(releaseChromeURL, chromePath); err != nil {
		return fmt.Errorf("failed to download chrome_elf.dll: %w", err)
	}

	i.setProgress(75)
	i.logf("Downloading latest blockthespot.dll.")
	if err := downloadFile(releaseBlockURL, blockPath); err != nil {
		return fmt.Errorf("failed to download blockthespot.dll: %w", err)
	}

	i.setProgress(90)
	i.logf("Writing latest config.ini.")
	if err := writeFileAtomically(configPath, i.downloadedConfig); err != nil {
		return fmt.Errorf("failed to write config.ini: %w", err)
	}

	i.setProgress(98)
	return nil
}

func startSpotify(exePath, workingDir string) error {
	cmd := exec.Command(exePath)
	cmd.Dir = workingDir
	return cmd.Start()
}

func loadAppIcon() (*walk.Icon, error) {
	if icon, err := walk.NewIconFromResourceId(1); err == nil {
		return icon, nil
	}

	exePath, err := os.Executable()
	if err != nil {
		return nil, err
	}
	return walk.NewIconExtractedFromFileWithSize(exePath, 0, 64)
}

func openExternalURL(url string) error {
	return exec.Command("rundll32", "url.dll,FileProtocolHandler", url).Start()
}

func hiddenCommand(name string, args ...string) *exec.Cmd {
	cmd := exec.Command(name, args...)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return cmd
}

func launchDetached(filePath string) error {
	cmd := exec.Command(filePath)
	if err := cmd.Start(); err != nil {
		return err
	}
	return cmd.Process.Release()
}

func (i *installer) loadConfig() error {
	body, err := downloadBytes(configURL)
	if err != nil {
		return fmt.Errorf("failed to download config.ini: %w", err)
	}

	version, err := extractMinimumVersionFromConfig(body)
	if err != nil {
		return fmt.Errorf("failed to parse minimum Spotify version from config.ini: %w", err)
	}

	i.minimumVersion = version
	i.downloadedConfig = body
	return nil
}

func fetchLatestInstallerRelease() (githubRelease, error) {
	req, err := http.NewRequest(http.MethodGet, installerLatestReleaseAPI, nil)
	if err != nil {
		return githubRelease{}, err
	}
	req.Header.Set("Accept", "application/vnd.github+json")
	req.Header.Set("User-Agent", "BlockTheSpotInstaller/"+installerVersion)

	client := &http.Client{Timeout: 10 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		return githubRelease{}, err
	}
	defer resp.Body.Close()

	if resp.StatusCode < http.StatusOK || resp.StatusCode >= http.StatusMultipleChoices {
		return githubRelease{}, fmt.Errorf("unexpected HTTP status %s", resp.Status)
	}

	var rel githubRelease
	if err := json.NewDecoder(resp.Body).Decode(&rel); err != nil {
		return githubRelease{}, err
	}
	if strings.TrimSpace(rel.TagName) == "" {
		return githubRelease{}, errors.New("latest release has no tag")
	}
	if strings.TrimSpace(rel.HTMLURL) == "" {
		rel.HTMLURL = installerReleasesURL
	}
	return rel, nil
}

func compareInstallerVersion(current, latest string) (int, error) {
	cv, err := parseInstallerVersion(current)
	if err != nil {
		return 0, err
	}
	lv, err := parseInstallerVersion(latest)
	if err != nil {
		return 0, err
	}

	maxLen := len(cv)
	if len(lv) > maxLen {
		maxLen = len(lv)
	}
	for len(cv) < maxLen {
		cv = append(cv, 0)
	}
	for len(lv) < maxLen {
		lv = append(lv, 0)
	}

	for idx := 0; idx < maxLen; idx++ {
		if cv[idx] < lv[idx] {
			return -1, nil
		}
		if cv[idx] > lv[idx] {
			return 1, nil
		}
	}
	return 0, nil
}

func parseInstallerVersion(value string) ([]int, error) {
	clean := strings.TrimSpace(value)
	clean = strings.TrimPrefix(clean, "v")
	if clean == "" {
		return nil, errors.New("empty version")
	}
	if strings.EqualFold(clean, "dev") {
		return nil, errors.New("dev build has no comparable release version")
	}

	if dash := strings.Index(clean, "-"); dash >= 0 {
		clean = clean[:dash]
	}

	parts := strings.Split(clean, ".")
	if len(parts) == 0 {
		return nil, errors.New("invalid version")
	}

	parsed := make([]int, 0, len(parts))
	for _, part := range parts {
		part = strings.TrimSpace(part)
		if part == "" {
			return nil, errors.New("invalid version component")
		}
		n, err := strconv.Atoi(part)
		if err != nil {
			return nil, err
		}
		parsed = append(parsed, n)
	}
	return parsed, nil
}

func stopSpotifyProcesses() {
	processes := []string{"Spotify.exe", "SpotifyWebHelper.exe", "SpotifyFullSetup.exe", "SpotifyFullSetupX64.exe"}
	for _, name := range processes {
		_ = hiddenCommand("taskkill", "/IM", name, "/F").Run()
	}
}

func isSpotifyStoreInstalled() (bool, error) {
	script := "$pkg = Get-AppxPackage -Name SpotifyAB.SpotifyMusic -ErrorAction SilentlyContinue; if ($pkg) { '1' } else { '0' }"
	out, err := hiddenCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if err != nil {
		return false, fmt.Errorf("powershell failed: %v (%s)", err, strings.TrimSpace(string(out)))
	}
	return strings.TrimSpace(string(out)) == "1", nil
}

func uninstallSpotifyStore() error {
	script := "$pkg = Get-AppxPackage -Name SpotifyAB.SpotifyMusic -ErrorAction SilentlyContinue; if ($pkg) { $pkg | Remove-AppxPackage -ErrorAction Stop }"
	out, err := hiddenCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if err != nil {
		return fmt.Errorf("powershell failed: %v (%s)", err, strings.TrimSpace(string(out)))
	}
	return nil
}

func isRunningAsAdmin() bool {
	script := "([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)"
	out, err := hiddenCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if err != nil {
		return false
	}
	return strings.EqualFold(strings.TrimSpace(string(out)), "True")
}

func runInstallerViaScheduledTask(setupPath string) error {
	taskName := fmt.Sprintf("Spotify install %d", time.Now().UnixNano())
	escapedTaskName := strings.ReplaceAll(taskName, "'", "''")
	escapedSetupPath := strings.ReplaceAll(setupPath, "'", "''")

	script := fmt.Sprintf("$apppath='powershell.exe'; $taskname='%s'; $action=New-ScheduledTaskAction -Execute $apppath -Argument \"-NoLogo -NoProfile -Command & '%s'\"; $trigger=New-ScheduledTaskTrigger -Once -At (Get-Date); $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -WakeToRun; Register-ScheduledTask -Action $action -Trigger $trigger -TaskName $taskname -Settings $settings -Force | Out-Null; Start-ScheduledTask -TaskName $taskname; Start-Sleep -Seconds 2; Unregister-ScheduledTask -TaskName $taskname -Confirm:$false", escapedTaskName, escapedSetupPath)

	out, err := hiddenCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if err != nil {
		return fmt.Errorf("powershell failed: %v (%s)", err, strings.TrimSpace(string(out)))
	}
	return nil
}

func getSpotifyVersion(spotifyExe string) (string, error) {
	escaped := strings.ReplaceAll(spotifyExe, "'", "''")
	script := fmt.Sprintf("$vi=(Get-Item '%s').VersionInfo; $v=$vi.ProductVersion; if (-not $v) { $v=[string]$vi.ProductVersionRaw }; [string]$v", escaped)
	out, err := hiddenCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script).CombinedOutput()
	if err != nil {
		return "", fmt.Errorf("powershell failed: %v (%s)", err, strings.TrimSpace(string(out)))
	}

	v := normalizeVersionString(strings.TrimSpace(string(out)))
	if v == "" {
		return "", errors.New("empty Spotify version")
	}
	return v, nil
}

func waitForFile(path string, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if fileExists(path) {
			return nil
		}
		time.Sleep(500 * time.Millisecond)
	}
	return fmt.Errorf("timed out waiting for %s", path)
}

func waitForProcess(name string, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		running, err := processRunning(name)
		if err == nil && running {
			return nil
		}
		time.Sleep(500 * time.Millisecond)
	}
	return fmt.Errorf("timed out waiting for process %s", name)
}

func processRunning(name string) (bool, error) {
	out, err := hiddenCommand("tasklist", "/FI", "IMAGENAME eq "+name, "/FO", "CSV", "/NH").CombinedOutput()
	if err != nil {
		return false, err
	}
	line := strings.TrimSpace(string(out))
	if line == "" || strings.Contains(line, "No tasks are running") {
		return false, nil
	}
	return strings.Contains(strings.ToLower(line), strings.ToLower(name)), nil
}

func removeIfExists(path string) error {
	if !fileExists(path) {
		return nil
	}
	return os.Remove(path)
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func defaultSpotifyDir() string {
	appData := os.Getenv("APPDATA")
	if appData != "" {
		return filepath.Join(appData, "Spotify")
	}

	home, err := os.UserHomeDir()
	if err != nil {
		return ""
	}
	return filepath.Join(home, "AppData", "Roaming", "Spotify")
}
