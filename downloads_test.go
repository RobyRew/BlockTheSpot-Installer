package main

import (
	"bytes"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestFetchCatalogOverHTTP(t *testing.T) {
	catalog := catalogFixture(t)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("User-Agent") == "" {
			t.Error("missing User-Agent")
		}
		switch r.URL.Path {
		case "/config":
			fmt.Fprint(w, ";Spotify for Windows (64 bit)\r\n;1.2.93.667\r\n")
		case "/versions":
			w.Write(catalog)
		default:
			http.NotFound(w, r)
		}
	}))
	defer server.Close()
	minimum, choices, selected, err := loadSpotifyInstallChoices(func(url string) ([]byte, error) {
		switch url {
		case configURL:
			return downloadBytes(server.URL + "/config")
		case spotifyVersionsURL:
			return downloadBytes(server.URL + "/versions")
		default:
			t.Fatalf("unexpected source %q", url)
			return nil, nil
		}
	})
	if err != nil {
		t.Fatal(err)
	}
	if minimum != "1.2.93.667" || choices[selected].BaseVersion != minimum {
		t.Fatalf("wrong selection for %s: %+v", minimum, choices)
	}
}

func TestSpotifyDownloadFollowsRedirectAndPreservesBytes(t *testing.T) {
	payload := append([]byte("MZ"), bytes.Repeat([]byte{42}, 6*1024*1024)...)
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/setup" {
			http.Redirect(w, r, "/file", http.StatusFound)
			return
		}
		w.Header().Set("Content-Length", fmt.Sprint(len(payload)))
		w.Write(payload)
	}))
	defer server.Close()
	target := filepath.Join(t.TempDir(), "SpotifySetup.exe")
	var logs []string
	choice := spotifyInstallChoice{URL: server.URL + "/setup", FullVersion: "1.2.93.667.g7b5cc0ce", Size: int64(len(payload))}
	err := downloadSpotifyInstaller(choice, target, func(format string, args ...any) { logs = append(logs, fmt.Sprintf(format, args...)) })
	if err != nil {
		t.Fatal(err)
	}
	got, err := os.ReadFile(target)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(got, payload) {
		t.Fatal("download contents changed")
	}
	if len(logs) < 3 {
		t.Fatalf("missing progress logs: %v", logs)
	}
	if _, err := os.Stat(target + ".download"); !os.IsNotExist(err) {
		t.Fatal("temporary download was not cleaned up")
	}
}

func TestMissingPinnedDownloadReportsRecoveryWithoutChangingVersion(t *testing.T) {
	for _, status := range []int{http.StatusNotFound, http.StatusGone, http.StatusForbidden} {
		t.Run(fmt.Sprint(status), func(t *testing.T) {
			requests := 0
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { requests++; w.WriteHeader(status) }))
			defer server.Close()
			target := filepath.Join(t.TempDir(), "setup.exe")
			err := downloadSpotifyInstaller(spotifyInstallChoice{FullVersion: "1.2.93.667.g7b5cc0ce", URL: server.URL}, target, nil)
			var httpErr *downloadHTTPError
			if !errors.As(err, &httpErr) || httpErr.StatusCode != status {
				t.Fatalf("missing HTTP error: %v", err)
			}
			if !strings.Contains(err.Error(), "Refresh versions") || !strings.Contains(err.Error(), "Latest official Spotify x64") {
				t.Fatalf("missing recovery steps: %v", err)
			}
			if requests != 1 {
				t.Fatal("download unexpectedly retried or substituted")
			}
			if _, err := os.Stat(target); !os.IsNotExist(err) {
				t.Fatal("failed download created an installer")
			}
		})
	}
}

func TestFailedDownloadDoesNotReplaceExistingFile(t *testing.T) {
	for _, tc := range []struct {
		name, body    string
		status        int
		contentLength string
	}{
		{"server error", "error", http.StatusInternalServerError, ""},
		{"HTML error page", "<html>Unavailable</html>", http.StatusOK, ""},
		{"empty response", "", http.StatusOK, ""},
		{"short executable header", "M", http.StatusOK, ""},
		{"truncated response", "MZpartial", http.StatusOK, "1000"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				if tc.contentLength != "" {
					w.Header().Set("Content-Length", tc.contentLength)
				}
				w.WriteHeader(tc.status)
				fmt.Fprint(w, tc.body)
			}))
			defer server.Close()
			target := filepath.Join(t.TempDir(), "setup.exe")
			if err := os.WriteFile(target, []byte("original"), 0o600); err != nil {
				t.Fatal(err)
			}
			if err := downloadFileWithProgress(server.URL, target, nil); err == nil {
				t.Fatal("invalid download succeeded")
			}
			got, err := os.ReadFile(target)
			if err != nil || string(got) != "original" {
				t.Fatalf("original file was lost: %q, %v", got, err)
			}
			if _, err := os.Stat(target + ".download"); !os.IsNotExist(err) {
				t.Fatal("partial download remains")
			}
		})
	}
}

func TestCatalogSizeMismatchRejectsDownload(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { fmt.Fprint(w, "MZpartial") }))
	defer server.Close()
	target := filepath.Join(t.TempDir(), "setup.exe")
	err := downloadSpotifyInstaller(spotifyInstallChoice{URL: server.URL, Size: 1000}, target, nil)
	if err == nil || !strings.Contains(err.Error(), "expected 1000") {
		t.Fatalf("size mismatch accepted: %v", err)
	}
	if _, err := os.Stat(target); !os.IsNotExist(err) {
		t.Fatal("incomplete installer remains")
	}
}

func TestPinnedChoiceWithoutURLDoesNotDownloadLatest(t *testing.T) {
	err := downloadSpotifyInstaller(spotifyInstallChoice{FullVersion: "1.2.93.667.g7b5cc0ce"}, filepath.Join(t.TempDir(), "setup.exe"), nil)
	if err == nil || !strings.Contains(err.Error(), "no download URL") {
		t.Fatalf("unexpected result: %v", err)
	}
}
