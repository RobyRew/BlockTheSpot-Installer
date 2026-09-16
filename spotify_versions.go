package main

import (
	"encoding/json"
	"fmt"
	"net/url"
	"regexp"
	"sort"
	"strings"
)

const (
	spotifySetupURL = "https://download.scdn.co/SpotifyFullSetupX64.exe"
	// This is the maintained catalog deployed at loadspot.pages.dev/versions.
	// LoaderSpot/LoaderSpot's older versions.json is archived and no longer updated.
	spotifyVersionsURL = "https://raw.githubusercontent.com/LoaderSpot/table/main/table/versions.json"
	configURL          = "https://github.com/Nuzair46/BlockTheSpot/releases/latest/download/config.ini"
)

type loaderSpotVersion struct {
	BuildType   string `json:"buildType"`
	FullVersion string `json:"fullversion"`
	Win         struct {
		X64 loaderSpotDownload `json:"x64"`
	} `json:"win"`
	Links struct {
		Win struct {
			X64 string `json:"x64"`
		} `json:"win"`
	} `json:"links"`
}

// LoadSpot's site accepts both compact objects and legacy string links.
type loaderSpotDownload struct {
	URL  string `json:"url"`
	Date string `json:"date"`
	Size int64  `json:"size"`
}

func (d *loaderSpotDownload) UnmarshalJSON(body []byte) error {
	if len(body) > 0 && body[0] == '"' {
		return json.Unmarshal(body, &d.URL)
	}
	type download loaderSpotDownload
	return json.Unmarshal(body, (*download)(d))
}

type spotifyInstallChoice struct {
	Display     string
	BaseVersion string
	FullVersion string
	URL         string
	Date        string
	Size        int64
	Recommended bool
}

func latestSpotifyInstallChoice() spotifyInstallChoice {
	return spotifyInstallChoice{
		Display: "Latest official Spotify x64",
		URL:     spotifySetupURL,
	}
}

func fetchSpotifyInstallChoices() (string, []spotifyInstallChoice, int, error) {
	return loadSpotifyInstallChoices(downloadBytes)
}

// Even when the catalog is unavailable or stale, the UI exposes a clearly labeled
// latest option. A selected pinned version is never silently replaced during download.
func loadSpotifyInstallChoices(download func(string) ([]byte, error)) (string, []spotifyInstallChoice, int, error) {
	fallback := []spotifyInstallChoice{latestSpotifyInstallChoice()}
	configBody, err := download(configURL)
	if err != nil {
		return "", fallback, 0, fmt.Errorf("failed to download config.ini: %w", err)
	}
	recommendedVersion, err := extractMinimumVersionFromConfig(configBody)
	if err != nil {
		return "", fallback, 0, fmt.Errorf("failed to parse recommended Spotify version: %w", err)
	}
	body, err := download(spotifyVersionsURL)
	if err != nil {
		return recommendedVersion, fallback, 0, fmt.Errorf("failed to download LoaderSpot catalog: %w", err)
	}
	var catalog map[string]loaderSpotVersion
	if err := json.Unmarshal(body, &catalog); err != nil {
		return recommendedVersion, fallback, 0, fmt.Errorf("failed to parse LoaderSpot catalog: %w", err)
	}
	choices, selectedIndex, err := buildSpotifyInstallChoices(recommendedVersion, catalog)
	return recommendedVersion, choices, selectedIndex, err
}

var spotifyVersionPattern = regexp.MustCompile(`^1\.[0-9]+\.[0-9]+\.[0-9]+(?:\.g[0-9a-fA-F]+)?$`)
var installerBuildPattern = regexp.MustCompile(`^[0-9]+\.exe$`)

func validLoaderSpotURL(rawURL, fullVersion string) bool {
	u, err := url.Parse(rawURL)
	if err != nil || u.Scheme != "https" || u.User != nil || u.RawQuery != "" || u.Fragment != "" || u.RawPath != "" {
		return false
	}
	if u.Host == "loadspot.amd64fox1.workers.dev" {
		return u.Path == "/download/spotify_installer-"+fullVersion+"-x64.exe"
	}
	if u.Host != "upgrade.scdn.co" {
		return false
	}
	prefix := "/upgrade/client/win32-x86_64/spotify_installer-" + fullVersion + "-"
	return strings.HasPrefix(u.Path, prefix) && installerBuildPattern.MatchString(strings.TrimPrefix(u.Path, prefix))
}

func buildSpotifyInstallChoices(recommendedVersion string, catalog map[string]loaderSpotVersion) ([]spotifyInstallChoice, int, error) {
	choices := []spotifyInstallChoice{latestSpotifyInstallChoice()}
	if !spotifyVersionPattern.MatchString(recommendedVersion) {
		return choices, 0, fmt.Errorf("invalid recommended Spotify version %q", recommendedVersion)
	}
	for baseVersion, entry := range catalog {
		fullVersion := strings.TrimSpace(entry.FullVersion)
		download := entry.Win.X64
		if download.URL == "" {
			download.URL = entry.Links.Win.X64
		}
		downloadURL := strings.TrimSpace(download.URL)
		buildType := strings.TrimSpace(entry.BuildType)
		if (buildType != "" && !strings.EqualFold(buildType, "Release")) ||
			!spotifyVersionPattern.MatchString(fullVersion) ||
			baseSpotifyVersion(fullVersion) != baseVersion ||
			compareVersion(baseVersion, recommendedVersion) < 0 ||
			!validLoaderSpotURL(downloadURL, fullVersion) {
			continue
		}
		choices = append(choices, spotifyInstallChoice{
			Display:     fullVersion,
			BaseVersion: baseVersion,
			FullVersion: fullVersion,
			URL:         downloadURL,
			Date:        download.Date,
			Size:        download.Size,
			Recommended: baseVersion == baseSpotifyVersion(recommendedVersion),
		})
	}
	if len(choices) == 1 {
		return choices, 0, fmt.Errorf("LoadSpot has no Windows x64 installers at or above %s; use 'Latest official Spotify x64'", recommendedVersion)
	}
	// Leave Latest first, with pinned releases sorted numerically from newest to oldest.
	pinned := choices[1:]
	sort.Slice(pinned, func(i, j int) bool {
		return compareVersion(pinned[i].BaseVersion, pinned[j].BaseVersion) > 0
	})
	selectedIndex := len(choices) - 1
	if choices[selectedIndex].Recommended {
		choices[selectedIndex].Display += " (recommended)"
	} else {
		choices[selectedIndex].Display += " (closest supported)"
	}
	for index := 1; index < len(choices); index++ {
		if choices[index].Date != "" {
			choices[index].Display += " | " + choices[index].Date
		}
		if choices[index].Size > 0 {
			choices[index].Display += fmt.Sprintf(" | %.2f MiB", bytesToMiB(choices[index].Size))
		}
	}
	return choices, selectedIndex, nil
}

func validateInstalledSpotifyVersion(installed, minimum string, selected spotifyInstallChoice) error {
	if !spotifyVersionPattern.MatchString(installed) {
		return fmt.Errorf("unable to verify installed Spotify version %q; patching stopped", installed)
	}
	if compareVersion(installed, minimum) < 0 {
		return fmt.Errorf("installed Spotify version %s is below supported minimum %s; patching stopped", installed, minimum)
	}
	if selected.FullVersion != "" && baseSpotifyVersion(installed) != baseSpotifyVersion(selected.FullVersion) {
		return fmt.Errorf("installed Spotify version %s does not match selected version %s; patching stopped", installed, selected.FullVersion)
	}
	return nil
}
