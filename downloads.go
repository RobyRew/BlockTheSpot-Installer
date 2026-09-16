package main

import (
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"time"
)

type downloadHTTPError struct {
	URL        string
	StatusCode int
	Status     string
}

func (e *downloadHTTPError) Error() string {
	return fmt.Sprintf("HTTP %s from %s", e.Status, e.URL)
}

func downloadSpotifyInstaller(choice spotifyInstallChoice, targetPath string, logf func(string, ...any)) error {
	if choice.URL == "" {
		if choice.FullVersion != "" {
			return fmt.Errorf("no download URL for Spotify %s; refresh the version list", choice.FullVersion)
		}
		choice = latestSpotifyInstallChoice()
	}
	if logf != nil {
		logf("Downloading Spotify installer: %s.", choice.Display)
	}
	if err := downloadFileWithProgress(choice.URL, targetPath, logf); err != nil {
		var statusErr *downloadHTTPError
		if errors.As(err, &statusErr) && (statusErr.StatusCode == http.StatusNotFound || statusErr.StatusCode == http.StatusGone || statusErr.StatusCode == http.StatusForbidden) {
			return fmt.Errorf("Spotify installer is unavailable: %w. Refresh versions or select 'Latest official Spotify x64' and try again", err)
		}
		return fmt.Errorf("failed to download Spotify installer: %w", err)
	}
	if choice.Size > 0 {
		info, err := os.Stat(targetPath)
		if err != nil {
			return err
		}
		if info.Size() != choice.Size {
			_ = os.Remove(targetPath)
			return fmt.Errorf("incomplete Spotify installer: got %d bytes, expected %d; refresh versions and try again", info.Size(), choice.Size)
		}
	}
	return nil
}

func downloadFile(url, targetPath string) error {
	body, err := downloadBytes(url)
	if err != nil {
		return err
	}
	return writeFileAtomically(targetPath, body)
}

func downloadFileWithProgress(url, targetPath string, logf func(format string, args ...any)) error {
	req, err := newDownloadRequest(url)
	if err != nil {
		return err
	}

	client := &http.Client{Timeout: 10 * time.Minute}
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode < http.StatusOK || resp.StatusCode >= http.StatusMultipleChoices {
		return &downloadHTTPError{URL: url, StatusCode: resp.StatusCode, Status: resp.Status}
	}

	tmpPath := targetPath + ".download"
	if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
		return err
	}

	file, err := os.Create(tmpPath)
	if err != nil {
		return err
	}
	defer os.Remove(tmpPath)

	// An HTTP 200 error page (or an empty response) must not be launched as setup.
	var signature [2]byte
	if _, err := io.ReadFull(resp.Body, signature[:]); err != nil {
		_ = file.Close()
		return fmt.Errorf("incomplete Spotify installer: %w", err)
	}
	if string(signature[:]) != "MZ" {
		_ = file.Close()
		return fmt.Errorf("download from %s is not a Windows executable", url)
	}
	if _, err := file.Write(signature[:]); err != nil {
		_ = file.Close()
		return err
	}

	buf := make([]byte, 256*1024)
	written := int64(len(signature))
	nextLogAt := int64(5 * 1024 * 1024)
	for {
		n, readErr := resp.Body.Read(buf)
		if n > 0 {
			if _, writeErr := file.Write(buf[:n]); writeErr != nil {
				_ = file.Close()
				_ = os.Remove(tmpPath)
				return writeErr
			}
			written += int64(n)
			if logf != nil && written >= nextLogAt {
				if resp.ContentLength > 0 {
					logf("Downloaded Spotify installer: %.1f MB / %.1f MB.", bytesToMiB(written), bytesToMiB(resp.ContentLength))
				} else {
					logf("Downloaded Spotify installer: %.1f MB.", bytesToMiB(written))
				}
				nextLogAt = written + int64(5*1024*1024)
			}
		}
		if readErr == io.EOF {
			break
		}
		if readErr != nil {
			_ = file.Close()
			_ = os.Remove(tmpPath)
			return readErr
		}
	}

	if err := file.Close(); err != nil {
		_ = os.Remove(tmpPath)
		return err
	}

	if logf != nil {
		logf("Spotify installer download complete: %.1f MB.", bytesToMiB(written))
	}

	_ = os.Remove(targetPath)
	if err := os.Rename(tmpPath, targetPath); err != nil {
		_ = os.Remove(tmpPath)
		return err
	}

	return nil
}

func bytesToMiB(value int64) float64 {
	return float64(value) / 1024 / 1024
}

func downloadBytes(url string) ([]byte, error) {
	req, err := newDownloadRequest(url)
	if err != nil {
		return nil, err
	}

	client := &http.Client{Timeout: 3 * time.Minute}
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode < http.StatusOK || resp.StatusCode >= http.StatusMultipleChoices {
		return nil, &downloadHTTPError{URL: url, StatusCode: resp.StatusCode, Status: resp.Status}
	}

	data, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	return data, nil
}

func newDownloadRequest(url string) (*http.Request, error) {
	req, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7")
	req.Header.Set("Accept-Language", "en-US,en;q=0.9")
	req.Header.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36")
	return req, nil
}

func writeFileAtomically(targetPath string, body []byte) error {
	tmpPath := targetPath + ".download"
	if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
		return err
	}

	file, err := os.Create(tmpPath)
	if err != nil {
		return err
	}

	_, copyErr := file.Write(body)
	closeErr := file.Close()
	if copyErr != nil {
		_ = os.Remove(tmpPath)
		return copyErr
	}
	if closeErr != nil {
		_ = os.Remove(tmpPath)
		return closeErr
	}

	_ = os.Remove(targetPath)
	if err := os.Rename(tmpPath, targetPath); err != nil {
		_ = os.Remove(tmpPath)
		return err
	}

	return nil
}
