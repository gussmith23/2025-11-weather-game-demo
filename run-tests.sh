#!/usr/bin/env bash
set -euo pipefail

UNITY="/Applications/Unity/Hub/Editor/6000.1.5f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS="$PROJECT/Logs/editmode-results.xml"
LOGFILE="$PROJECT/Logs/editmode-log.txt"

"$UNITY" \
  -batchmode \
  -projectPath "$PROJECT" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$RESULTS" \
  -logFile "$LOGFILE"

echo "Test results: $RESULTS"
