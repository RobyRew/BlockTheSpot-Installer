package main

import (
	"encoding/json"
	"errors"
	"os"
	"strings"
	"testing"
)

func catalogFixture(t *testing.T) []byte {
	t.Helper()
	body, err := os.ReadFile("testdata/loadspot_versions.json")
	if err != nil {
		t.Fatal(err)
	}
	return body
}

func TestLiveCatalogFormatAndScreenshotRegression(t *testing.T) {
	var catalog map[string]loaderSpotVersion
	if err := json.Unmarshal(catalogFixture(t), &catalog); err != nil {
		t.Fatal(err)
	}
	choices, selected, err := buildSpotifyInstallChoices("1.2.93.667", catalog)
	if err != nil {
		t.Fatal(err)
	}
	if len(choices) != 4 || selected != 3 {
		t.Fatalf("got %d choices, selected %d", len(choices), selected)
	}
	if choices[0].URL != spotifySetupURL || choices[0].FullVersion != "" {
		t.Fatal("missing explicit latest option")
	}
	if choices[1].FullVersion != "1.3.1.223.g6311b0a4" || choices[2].BaseVersion != "1.3.0.277" {
		t.Fatal("versions are not sorted numerically")
	}
	recommended := choices[selected]
	// This exact version failed in the user's screenshot. Preserve the supplied URL.
	want := "https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe"
	if recommended.URL != want || !recommended.Recommended {
		t.Fatalf("incorrect recommended choice: %+v", recommended)
	}
	if recommended.Size != 146096232 || recommended.Date != "01.07.2026" || !strings.Contains(recommended.Display, "(recommended)") {
		t.Fatalf("lost catalog metadata: %+v", recommended)
	}
}

func TestCatalogFiltersWrongArchitectureUnsupportedAndInvalidLinks(t *testing.T) {
	body := `{
	 "1.2.93.667": {"fullversion":"1.2.93.667.g7b5cc0ce","win":{"x64":"https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.93.667.g7b5cc0ce-x64.exe"}},
	 "1.2.94.100": {"fullversion":"1.2.94.100.gabc123","win":{"arm64":{"url":"https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.94.100.gabc123-arm64.exe"}}},
	 "1.2.94.101": {"fullversion":"1.2.94.101.gabc123","win":{"x64":{"url":"https://example.com/setup.exe"}}},
	 "1.2.94.102": {"fullversion":"1.2.94.102.gabc123","buildType":"Master","links":{"win":{"x64":"https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-1.2.94.102.gabc123-123.exe"}}},
	 "1.2.94.103": {"fullversion":"1.2.94.104.gabc123","win":{"x64":{"url":"https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.94.104.gabc123-x64.exe"}}},
	 "1.2.92.100": {"fullversion":"1.2.92.100.gabc123","win":{"x64":{"url":"https://loadspot.amd64fox1.workers.dev/download/spotify_installer-1.2.92.100.gabc123-x64.exe"}}}
	}`
	var catalog map[string]loaderSpotVersion
	if err := json.Unmarshal([]byte(body), &catalog); err != nil {
		t.Fatal(err)
	}
	choices, selected, err := buildSpotifyInstallChoices("1.2.93.667", catalog)
	if err != nil {
		t.Fatal(err)
	}
	if len(choices) != 2 || selected != 1 {
		t.Fatalf("unexpected choices: %+v", choices)
	}
}

func TestLegacyCatalogAndClosestSupportedChoice(t *testing.T) {
	var catalog map[string]loaderSpotVersion
	err := json.Unmarshal([]byte(`{"1.2.85.519":{"buildType":"Release","fullversion":"1.2.85.519.g549a528b","links":{"win":{"x64":"https://upgrade.scdn.co/upgrade/client/win32-x86_64/spotify_installer-1.2.85.519.g549a528b-4062.exe"}}}}`), &catalog)
	if err != nil {
		t.Fatal(err)
	}
	choices, selected, err := buildSpotifyInstallChoices("1.2.85.500", catalog)
	if err != nil {
		t.Fatal(err)
	}
	if selected != 1 || choices[selected].Recommended || !strings.Contains(choices[selected].Display, "closest supported") {
		t.Fatalf("incorrect nearest choice: %+v", choices)
	}
	if !strings.HasSuffix(choices[selected].URL, "-4062.exe") {
		t.Fatal("installer build suffix was changed")
	}
}

func TestCatalogFailureKeepsExplicitLatestOption(t *testing.T) {
	for _, tc := range []struct{ name, config, catalog, failURL string }{
		{"config unavailable", "", "", configURL},
		{"invalid config", ";not a version", "{}", ""},
		{"catalog unavailable", ";1.2.93.667", "", spotifyVersionsURL},
		{"malformed catalog", ";1.2.93.667", "<html>error</html>", ""},
		{"empty catalog", ";1.2.93.667", "{}", ""},
		{"null catalog", ";1.2.93.667", "null", ""},
		{"stale catalog", ";1.9.0.0", string(catalogFixture(t)), ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			_, choices, selected, err := loadSpotifyInstallChoices(func(url string) ([]byte, error) {
				if url == tc.failURL {
					return nil, errors.New("network unavailable")
				}
				if url == configURL {
					return []byte(tc.config), nil
				}
				return []byte(tc.catalog), nil
			})
			if err == nil {
				t.Fatal("expected diagnostic for missing pinned versions")
			}
			if len(choices) != 1 || selected != 0 || choices[0] != latestSpotifyInstallChoice() {
				t.Fatalf("incorrect fallback: %+v", choices)
			}
		})
	}
}

func TestLoaderSpotURLValidation(t *testing.T) {
	version := "1.2.93.667.g7b5cc0ce"
	valid := "https://loadspot.amd64fox1.workers.dev/download/spotify_installer-" + version + "-x64.exe"
	if !validLoaderSpotURL(valid, version) {
		t.Fatal("valid LoadSpot URL rejected")
	}
	for _, invalid := range []string{
		strings.Replace(valid, "https:", "http:", 1),
		strings.Replace(valid, ".dev/", ".dev.evil.test/", 1),
		strings.Replace(valid, "x64.exe", "arm64.exe", 1),
		strings.Replace(valid, version, "1.2.93.666.g7b5cc0ce", 1),
		strings.Replace(valid, "https://", "https://user:pass@", 1),
		valid + "?redirect=example.com", valid + "#fragment",
		strings.Replace(valid, ".dev/", ".dev:8443/", 1),
	} {
		if validLoaderSpotURL(invalid, version) {
			t.Errorf("accepted invalid URL %q", invalid)
		}
	}
}

func TestInstalledVersionMustMatchSelectionAndMinimum(t *testing.T) {
	pinned := spotifyInstallChoice{FullVersion: "1.2.93.667.g7b5cc0ce"}
	for _, tc := range []struct {
		installed string
		choice    spotifyInstallChoice
		wantErr   bool
	}{
		{"1.2.93.667", pinned, false},
		{"1.2.93.667.g7b5cc0ce", pinned, false},
		{"1.2.92.100", pinned, true},
		{"1.3.1.223", pinned, true},
		{"1.3.1.223", latestSpotifyInstallChoice(), false},
		{"1.2.92.100", latestSpotifyInstallChoice(), true},
		{"unknown", latestSpotifyInstallChoice(), true},
	} {
		t.Run(tc.installed+tc.choice.FullVersion, func(t *testing.T) {
			err := validateInstalledSpotifyVersion(tc.installed, "1.2.93.667", tc.choice)
			if (err != nil) != tc.wantErr {
				t.Fatalf("unexpected validation result: %v", err)
			}
		})
	}
}

func TestConfigVersionIsUsedWithoutTemporaryOverride(t *testing.T) {
	for _, version := range []string{"1.2.93.667", "1.2.93.667.g7b5cc0ce", "1.3.1.223"} {
		got, err := extractMinimumVersionFromConfig([]byte(";Spotify for Windows (64 bit)\r\n;" + version + "\r\n[Log]\r\nLevel=0\r\n"))
		if err != nil || got != version {
			t.Fatalf("got %q, %v; want %s", got, err, version)
		}
	}
}

// Opt in for a current upstream smoke check; ordinary tests never need the network.
func TestLiveLoadSpotCatalog(t *testing.T) {
	if os.Getenv("BLOCKTHESPOT_LIVE_TEST") != "1" {
		t.Skip("set BLOCKTHESPOT_LIVE_TEST=1 for live catalog verification")
	}
	minimum, choices, selected, err := fetchSpotifyInstallChoices()
	if err != nil {
		t.Fatal(err)
	}
	if len(choices) < 2 || selected < 1 {
		t.Fatalf("no supported pinned versions for %s", minimum)
	}
	t.Logf("minimum=%s, pinned releases=%d, selected=%s, newest=%s", minimum, len(choices)-1, choices[selected].FullVersion, choices[1].FullVersion)
}
