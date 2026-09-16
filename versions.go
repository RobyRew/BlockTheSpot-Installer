package main

import (
	"errors"
	"strconv"
	"strings"
)

func extractMinimumVersionFromConfig(body []byte) (string, error) {
	lines := strings.Split(string(body), "\n")
	for _, line := range lines {
		line = strings.TrimSpace(line)
		if !strings.HasPrefix(line, ";") {
			continue
		}

		candidate := strings.TrimSpace(strings.TrimPrefix(line, ";"))
		if candidate == "" || !looksLikeSpotifyVersion(candidate) {
			continue
		}
		return candidate, nil
	}

	return "", errors.New("no Spotify version marker found")
}

func looksLikeSpotifyVersion(value string) bool {
	return spotifyVersionPattern.MatchString(value)
}

func compareVersion(a, b string) int {
	av := normalizeVersion(a)
	bv := normalizeVersion(b)
	for idx := 0; idx < len(av) && idx < len(bv); idx++ {
		if av[idx] < bv[idx] {
			return -1
		}
		if av[idx] > bv[idx] {
			return 1
		}
	}
	return 0
}

func normalizeVersion(value string) []int {
	parts := strings.Split(value, ".")
	parsed := make([]int, 0, 4)
	for _, part := range parts {
		digits := leadingDigits(part)
		if digits == "" {
			break
		}
		n, err := strconv.Atoi(digits)
		if err != nil {
			break
		}
		parsed = append(parsed, n)
		if len(parsed) == 4 {
			break
		}
	}
	for len(parsed) < 4 {
		parsed = append(parsed, 0)
	}
	return parsed
}

func leadingDigits(s string) string {
	var b strings.Builder
	for _, r := range s {
		if r < '0' || r > '9' {
			break
		}
		b.WriteRune(r)
	}
	return b.String()
}

func normalizeVersionString(raw string) string {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return ""
	}

	// Most systems return a dotted version directly.
	for _, token := range strings.Fields(raw) {
		if strings.Count(token, ".") >= 2 && leadingDigits(token) != "" {
			return token
		}
	}

	// Some PowerShell setups format ProductVersionRaw as a table (Major Minor Build Revision).
	numbers := make([]string, 0, 4)
	for _, token := range strings.Fields(raw) {
		if !isAllDigits(token) {
			continue
		}
		numbers = append(numbers, token)
		if len(numbers) == 4 {
			break
		}
	}
	if len(numbers) >= 3 {
		return strings.Join(numbers, ".")
	}

	return raw
}

func baseSpotifyVersion(value string) string {
	parts := strings.Split(value, ".")
	base := make([]string, 0, 4)
	for _, part := range parts {
		digits := leadingDigits(part)
		if digits == "" {
			break
		}
		base = append(base, digits)
		if len(base) == 4 {
			break
		}
	}
	return strings.Join(base, ".")
}

func isAllDigits(value string) bool {
	if value == "" {
		return false
	}
	for _, r := range value {
		if r < '0' || r > '9' {
			return false
		}
	}
	return true
}
