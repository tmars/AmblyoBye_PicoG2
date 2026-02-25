#!/bin/zsh
# Pico G2 — утилиты управления
# Использование: ./pico.sh <команда> [аргументы]

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VIDEOS_DIR="$SCRIPT_DIR/videos"
DEVICE_VIDEOS="/sdcard/Android/data/com.amblyobye.amblyobye/Movies"
APK="$SCRIPT_DIR/Builds/AmblyoBye_PicoG2.apk"
PACKAGE="com.amblyobye.amblyobye"

# --- Проверка устройства ---
check_device() {
  if ! adb devices | grep -q "device$"; then
    echo "❌ Устройство не подключено. Подключи Pico G2 по USB."
    exit 1
  fi
}

# --- Команды ---

cmd_update() {
  echo "📦 Обновление APK..."
  if [ ! -f "$APK" ]; then
    echo "❌ APK не найден: $APK"
    exit 1
  fi
  check_device
  adb install -r "$APK" && echo "✅ APK установлен"
}

cmd_upload() {
  local arg="$1"
  if [ -z "$arg" ]; then
    echo "Использование: ./pico.sh upload <название.mp4 или /полный/путь>"
    echo ""
    echo "Видео в $VIDEOS_DIR:"
    ls "$VIDEOS_DIR"/*.mp4 2>/dev/null | xargs -I{} basename "{}"
    exit 1
  fi

  # Если передан полный путь — используем как есть, иначе ищем в videos/
  if [[ "$arg" == /* ]]; then
    local file="$arg"
  else
    local file="$VIDEOS_DIR/$arg"
  fi

  if [ ! -f "$file" ]; then
    echo "❌ Файл не найден: $file"
    exit 1
  fi

  check_device
  echo "📤 Загружаю: $(basename "$file")"
  adb push "$file" "$DEVICE_VIDEOS/" && echo "✅ Загружено в $DEVICE_VIDEOS"
}

cmd_sync_all() {
  check_device

  echo ""
  echo "📂 Локальные видео: $VIDEOS_DIR"
  echo "📱 Папка на устройстве: $DEVICE_VIDEOS"
  echo ""

  # Получаем список файлов на устройстве
  local device_files
  device_files=$(adb shell ls "$DEVICE_VIDEOS/" 2>/dev/null | tr -d '\r')

  # Собираем все локальные видео файлы
  local video_exts=("mp4" "mkv" "avi" "mov" "webm" "wmv" "mpg" "mpeg")
  local local_files=()
  for ext in "${video_exts[@]}"; do
    for f in "$VIDEOS_DIR"/*.$ext(N); do
      local_files+=("$f")
    done
  done

  if [ ${#local_files[@]} -eq 0 ]; then
    echo "⚠️  В папке videos/ нет видео файлов"
    exit 0
  fi

  # Показываем статус локальных файлов
  echo "Статус видео (локальные → устройство):"
  echo "─────────────────────────────────────────────────────"
  local missing=()
  for file in "${local_files[@]}"; do
    local name="$(basename "$file")"
    if echo "$device_files" | grep -qF "$name"; then
      printf "  ✅  %s\n" "$name"
    else
      printf "  ❌  %s\n" "$name"
      missing+=("$file")
    fi
  done
  echo "─────────────────────────────────────────────────────"

  # Показываем файлы только на устройстве (которых нет локально)
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
    echo "Только на устройстве (нет в local videos/):"
    for f in "${only_on_device[@]}"; do
      printf "  📱  %s\n" "$f"
    done
    echo "─────────────────────────────────────────────────────"
  fi
  echo ""

  if [ ${#missing[@]} -eq 0 ]; then
    echo "✅ Все видео уже на устройстве. Ничего загружать не нужно."
    exit 0
  fi

  echo "⬆️  Загружаю ${#missing[@]} недостающих файлов..."
  echo ""
  local ok=0
  local fail=0
  for file in "${missing[@]}"; do
    local name="$(basename "$file")"
    printf "  📤 %s ... " "$name"
    if adb push "$file" "$DEVICE_VIDEOS/" > /dev/null 2>&1; then
      printf "✅\n"
      (( ok++ ))
    else
      printf "❌ ошибка\n"
      (( fail++ ))
    fi
  done

  echo ""
  echo "Готово: загружено $ok, ошибок $fail"
}

cmd_build() {
  local UNITY="/Applications/Unity/Hub/Editor/2021.3.0f1/Unity.app/Contents/MacOS/Unity"
  local PROJECT="$SCRIPT_DIR"
  local LOG="/tmp/unity-build.log"
  local RESULT="/tmp/unity-auto-build-result"

  if [ ! -f "$UNITY" ]; then
    echo "❌ Unity не найден: $UNITY"
    exit 1
  fi

  # Ставим триггер для AutoBuildOnLoad (он сработает при запуске редактора)
  rm -f "$RESULT"
  touch "/tmp/unity-auto-build-trigger"

  echo "🔨 Запускаю Unity для сборки..."
  echo "   Лог: $LOG"
  echo "   (Unity откроется на несколько минут и закроется автоматически)"
  echo ""

  # Без -batchmode: обходит проблему с лицензией, GUI мелькнёт и закроется
  "$UNITY" \
    -quit \
    -projectPath "$PROJECT" \
    -logFile "$LOG" \
    2>/dev/null &

  local PID=$!
  echo "   PID: $PID"
  echo ""

  # Ждём завершения с прогрессом
  local i=0
  local spin=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
  while kill -0 $PID 2>/dev/null; do
    # Показываем текущую фазу из лога если доступна
    local phase=""
    if [ -f "$LOG" ]; then
      phase=$(grep -o "Starting CreateSceneAndBuild\|Build completed\|Compiling\|Building" "$LOG" 2>/dev/null | tail -1)
    fi
    printf "\r   %s  %ds  %s          " "${spin[$((i % 10))]}" "$i" "$phase"
    sleep 1
    (( i++ ))
  done
  printf "\r                                          \r"

  wait $PID
  local EXIT_CODE=$?

  echo ""

  # Проверяем результат через файл или APK
  if [ -f "$RESULT" ]; then
    local result_content=$(cat "$RESULT")
    if [[ "$result_content" == "SUCCESS" ]]; then
      local SIZE=$(du -sh "$APK" 2>/dev/null | cut -f1)
      echo "✅ Сборка успешна! APK: $APK ($SIZE)"
      return 0
    else
      echo "❌ Сборка завершилась с ошибкой: $result_content"
    fi
  elif [ $EXIT_CODE -eq 0 ] && [ -f "$APK" ]; then
    local SIZE=$(du -sh "$APK" | cut -f1)
    echo "✅ APK собран: $APK ($SIZE)"
    return 0
  else
    echo "❌ Сборка завершилась с ошибкой (код $EXIT_CODE)"
    echo ""
    echo "Последние строки лога:"
    tail -30 "$LOG"
    exit 1
  fi
}

cmd_build_and_deploy() {
  cmd_build && cmd_update && cmd_launch
}

cmd_launch() {
  echo "🚀 Запуск AmblyoBye..."
  check_device
  adb shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 > /dev/null && echo "✅ Приложение запущено"
}

# --- Справка ---
cmd_help() {
  echo ""
  echo "Pico G2 — утилиты"
  echo ""
  echo "  ./pico.sh build               — собрать APK через Unity"
  echo "  ./pico.sh deploy              — build + install + launch одной командой"
  echo "  ./pico.sh update              — установить/обновить APK на устройстве"
  echo "  ./pico.sh upload <файл>       — загрузить видео (имя из videos/ или полный путь)"
  echo "  ./pico.sh upload all          — показать статус и загрузить недостающие видео"
  echo "  ./pico.sh launch              — запустить AmblyoBye на устройстве"
  echo ""
  echo "Примеры:"
  echo "  ./pico.sh build"
  echo "  ./pico.sh deploy"
  echo "  ./pico.sh upload all"
  echo "  ./pico.sh upload movie.mp4"
  echo "  ./pico.sh upload /Users/me/Downloads/film.mp4"
  echo ""
}

# --- Роутер ---
case "$1" in
  build)    cmd_build ;;
  deploy)   cmd_build_and_deploy ;;
  update)   cmd_update ;;
  upload)
    if [ "$2" = "all" ]; then cmd_sync_all
    else cmd_upload "$2"
    fi ;;
  launch)   cmd_launch ;;
  *)        cmd_help ;;
esac
