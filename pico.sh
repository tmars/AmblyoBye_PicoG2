#!/bin/zsh
# Pico G2 — CLI helper for AmblyoBye
# Usage: ./pico.sh <command> [arguments]

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VIDEOS_DIR="$SCRIPT_DIR/videos"
DEVICE_VIDEOS="/sdcard/Android/data/com.amblyobye.amblyobye/Movies"
APK="$SCRIPT_DIR/Builds/AmblyoBye_PicoG2.apk"
PACKAGE="com.amblyobye.amblyobye"
YTDLP_DIR="$SCRIPT_DIR/tools/yt-dlp"
YTDLP_VENV="$YTDLP_DIR/venv/bin/python3"
YTDLP_SCRIPT="$YTDLP_DIR/download.py"

# --- Device check ---
check_device() {
  if ! adb devices | grep -q "device$"; then
    echo "Device not connected. Connect Pico G2 via USB."
    exit 1
  fi
}

# --- Commands ---

cmd_update() {
  echo "Installing APK..."
  if [ ! -f "$APK" ]; then
    echo "APK not found: $APK"
    exit 1
  fi
  check_device
  adb install -r "$APK" && echo "APK installed"
}

cmd_upload() {
  local arg="$1"
  if [ -z "$arg" ]; then
    echo "Usage: ./pico.sh upload <file.mp4 or /full/path>"
    echo ""
    echo "Videos in $VIDEOS_DIR:"
    ls "$VIDEOS_DIR"/*.mp4 2>/dev/null | xargs -I{} basename "{}"
    exit 1
  fi

  if [[ "$arg" == /* ]]; then
    local file="$arg"
  else
    local file="$VIDEOS_DIR/$arg"
  fi

  if [ ! -f "$file" ]; then
    echo "File not found: $file"
    exit 1
  fi

  check_device
  echo "Uploading: $(basename "$file")"
  adb push "$file" "$DEVICE_VIDEOS/" && echo "Uploaded to $DEVICE_VIDEOS"
}

cmd_sync_all() {
  check_device

  echo ""
  echo "Local videos: $VIDEOS_DIR"
  echo "Device path:  $DEVICE_VIDEOS"
  echo ""

  local device_files
  device_files=$(adb shell ls "$DEVICE_VIDEOS/" 2>/dev/null | tr -d '\r')

  local video_exts=("mp4" "mkv" "avi" "mov" "webm" "wmv" "mpg" "mpeg")
  local local_files=()
  for ext in "${video_exts[@]}"; do
    for f in "$VIDEOS_DIR"/*.$ext(N); do
      local_files+=("$f")
    done
  done

  if [ ${#local_files[@]} -eq 0 ]; then
    echo "No video files in videos/"
    exit 0
  fi

  echo "Sync status (local -> device):"
  echo "---------------------------------------------------"
  local missing=()
  for file in "${local_files[@]}"; do
    local name="$(basename "$file")"
    if echo "$device_files" | grep -qF "$name"; then
      printf "  [ok]   %s\n" "$name"
    else
      printf "  [--]   %s\n" "$name"
      missing+=("$file")
    fi
  done
  echo "---------------------------------------------------"

  local only_on_device=()
  while IFS= read -r dfile; do
    [ -z "$dfile" ] && continue
    local found=0
    for file in "${local_files[@]}"; do
      [[ "$(basename "$file")" == "$dfile" ]] && found=1 && break
    done
    [[ $found -eq 0 ]] && only_on_device+=("$dfile")
  done <<< "$device_files"

  if [ ${#only_on_device[@]} -gt 0 ]; then
    echo ""
    echo "Only on device (not in local videos/):"
    for f in "${only_on_device[@]}"; do
      printf "  [dev]  %s\n" "$f"
    done
    echo "---------------------------------------------------"
  fi
  echo ""

  if [ ${#missing[@]} -eq 0 ]; then
    echo "All videos are already on the device."
    exit 0
  fi

  echo "Uploading ${#missing[@]} missing files..."
  echo ""
  local ok=0
  local fail=0
  for file in "${missing[@]}"; do
    local name="$(basename "$file")"
    printf "  %s ... " "$name"
    if adb push "$file" "$DEVICE_VIDEOS/" > /dev/null 2>&1; then
      printf "ok\n"
      (( ok++ ))
    else
      printf "FAIL\n"
      (( fail++ ))
    fi
  done

  echo ""
  echo "Done: uploaded $ok, failed $fail"
}

cmd_build() {
  local UNITY="/Applications/Unity/Hub/Editor/2021.3.0f1/Unity.app/Contents/MacOS/Unity"
  local PROJECT="$SCRIPT_DIR"
  local LOG="/tmp/unity-build.log"
  local RESULT="/tmp/unity-auto-build-result"

  if [ ! -f "$UNITY" ]; then
    echo "Unity not found: $UNITY"
    exit 1
  fi

  rm -f "$RESULT"
  touch "/tmp/unity-auto-build-trigger"

  echo "Building APK..."
  echo "  Log: $LOG"
  echo "  (Unity will open briefly and close automatically)"
  echo ""

  "$UNITY" \
    -quit \
    -projectPath "$PROJECT" \
    -logFile "$LOG" \
    2>/dev/null &

  local PID=$!
  echo "  PID: $PID"
  echo ""

  local i=0
  local spin=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
  while kill -0 $PID 2>/dev/null; do
    local phase=""
    if [ -f "$LOG" ]; then
      phase=$(grep -o "Starting CreateSceneAndBuild\|Build completed\|Compiling\|Building" "$LOG" 2>/dev/null | tail -1)
    fi
    printf "\r  %s  %ds  %s          " "${spin[$((i % 10))]}" "$i" "$phase"
    sleep 1
    (( i++ ))
  done
  printf "\r                                          \r"

  wait $PID
  local EXIT_CODE=$?

  echo ""

  if [ -f "$RESULT" ]; then
    local result_content=$(cat "$RESULT")
    if [[ "$result_content" == "SUCCESS" ]]; then
      local SIZE=$(du -sh "$APK" 2>/dev/null | cut -f1)
      echo "Build successful! APK: $APK ($SIZE)"
      return 0
    else
      echo "Build failed: $result_content"
    fi
  elif [ $EXIT_CODE -eq 0 ] && [ -f "$APK" ]; then
    local SIZE=$(du -sh "$APK" | cut -f1)
    echo "APK built: $APK ($SIZE)"
    return 0
  else
    echo "Build failed (exit code $EXIT_CODE)"
    echo ""
    echo "Last 30 lines of log:"
    tail -30 "$LOG"
    exit 1
  fi
}

cmd_build_and_deploy() {
  cmd_build && cmd_update && cmd_launch
}

cmd_launch() {
  echo "Launching AmblyoBye..."
  check_device
  adb shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 > /dev/null && echo "App launched"
}

cmd_download() {
  local url="$1"
  if [ -z "$url" ]; then
    echo "Usage: ./pico.sh download <URL>"
    echo ""
    echo "Downloads video to videos/ folder using yt-dlp."
    echo "Supports YouTube, VK, and most video sites."
    exit 1
  fi

  # Auto-setup venv if not present
  if [ ! -f "$YTDLP_VENV" ]; then
    echo "Setting up yt-dlp (first run)..."
    python3 -m venv "$YTDLP_DIR/venv"
    "$YTDLP_VENV" -m pip install --quiet -r "$YTDLP_DIR/requirements.txt"
    echo "Setup complete."
    echo ""
  fi

  mkdir -p "$VIDEOS_DIR"
  "$YTDLP_VENV" "$YTDLP_SCRIPT" "$url" -o "$VIDEOS_DIR"
}

# --- Help ---
cmd_help() {
  echo ""
  echo "AmblyoBye Pico G2 — CLI helper"
  echo ""
  echo "  ./pico.sh build               Build APK via Unity"
  echo "  ./pico.sh deploy              Build + install + launch (all-in-one)"
  echo "  ./pico.sh update              Install/update APK on device"
  echo "  ./pico.sh upload <file>       Upload video (name from videos/ or full path)"
  echo "  ./pico.sh upload all          Show sync status and upload missing videos"
  echo "  ./pico.sh download <URL>      Download video from URL to videos/ folder"
  echo "  ./pico.sh launch              Launch app on device"
  echo ""
  echo "Examples:"
  echo "  ./pico.sh build"
  echo "  ./pico.sh deploy"
  echo "  ./pico.sh upload all"
  echo "  ./pico.sh upload movie.mp4"
  echo "  ./pico.sh download https://youtube.com/watch?v=..."
  echo ""
}

# --- Router ---
case "$1" in
  build)    cmd_build ;;
  deploy)   cmd_build_and_deploy ;;
  update)   cmd_update ;;
  upload)
    if [ "$2" = "all" ]; then cmd_sync_all
    else cmd_upload "$2"
    fi ;;
  download) cmd_download "$2" ;;
  launch)   cmd_launch ;;
  *)        cmd_help ;;
esac
