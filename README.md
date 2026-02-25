# AmblyoBye — Pico G2 4K Port

**AmblyoBye** is a virtual reality (VR) application designed to assist in the treatment of amblyopia (commonly known as "lazy eye") through dichoptic movie viewing.

This is a port of [AmblyoBye](https://github.com/alexmuraru27/AmblyoBye) by Alex Muraru (originally for Meta Quest 2/3, Unity 6) to **Pico G2 4K** (3DoF headset, Unity 2021.3 LTS, Pico Legacy VR SDK).

---

## Purpose

AmblyoBye is inspired by research indicating that dichoptic movie viewing can enhance visual function in amblyopic patients, even beyond the critical period of visual development.

Studies have shown that presenting different images to each eye can promote binocular vision and reduce suppression of the amblyopic eye ([source](https://pubmed.ncbi.nlm.nih.gov/31558900/)).

The app plays videos with a dichoptic filter — each eye sees a different set of "puzzle pieces" (blob pattern). The brain is forced to use both eyes to see the full picture, which helps treat amblyopia.

---

## Supported Devices

- **Pico G2 4K** (this port)
- **Pico G2 4K Enterprise** (this port)
- Meta Quest 2 — see [original repository](https://github.com/alexmuraru27/AmblyoBye)
- Meta Quest 3 — see [original repository](https://github.com/alexmuraru27/AmblyoBye)

---

## Features

**Dichoptic rendering:**
- Complementary dichoptic rendering — each eye sees inverted blob pattern
- Adjustable Eye Bias, Blob Scale, Blob Gray Color, Blob Change Timer

**Video playback:**
- Video picker with pagination
- Seek controls: 5s, 10s, 1m, 5m, 10m forward/back
- Pause/Play
- Video playback position saved per file (resume where you left off)
- Last played video remembered across sessions

**VR UI:**
- Gaze-based UI — look at buttons and press to click
- Works with controller (touchpad) **and without** (headset body button)
- VR-friendly +/- buttons instead of sliders
- Screen distance adjustment (closer/farther)
- All settings persist between sessions
- Black background for minimal distraction
- Session watch time tracking

---

## Screenshots

|       **Left Eye View**        |        **Right Eye View**        |
| :----------------------------: | :------------------------------: |
| Each eye sees different puzzle pieces | Inverted blob pattern per eye |

> Screenshots from the original Quest version are available in the [original repository](https://github.com/alexmuraru27/AmblyoBye). The dichoptic effect is identical on Pico G2.

---

## In-App Settings

- **Eye Bias** — adjusts the infill percentage of the blobs.
  - 0 → full suppression on the left eye
  - 0.5 → balanced (both eyes see equal amount)
  - 1.0 → full suppression on the right eye
  - For left eye amblyopia, use values > 0.5 to suppress the right eye more
- **Blob Scale** — size of the blob-shaped contrast mask
- **Blob Color** — how dark/light the blobs appear (0 = black, 255 = white)
- **Blob Timer** — how often the blob mask changes position (in seconds)
- **Screen Distance** — move the video screen closer or farther

### Suggested starting settings

| Setting | Value |
|---------|-------|
| Eye Bias | 0.50 |
| Blob Scale | 1.0 |
| Blob Color | 70 |
| Blob Timer | 5 |

---

## Controls

### With controller
- **Touchpad click**: Show/hide settings menu, click UI buttons

### Without controller (headset body buttons)
- **Back button**: Show/hide settings menu, click UI buttons
- **Volume buttons**: System volume (handled by OS)

### In settings menu
Look at a button with the gaze reticle (white dot in center), then press touchpad or back button to click.

---

## Installation & Setup

### Requirements

- **Unity 2021.3.0f1** (LTS) — other 2021.3.x may work
- **Android SDK** (API 26+)
- **Android NDK r21d** (required for IL2CPP / ARM64 build)
- **Pico G2 4K** headset

### 1. Install Unity

Install **Unity 2021.3.0f1** via Unity Hub with modules:
- Android Build Support
- Android SDK & NDK Tools (or provide your own)
- OpenJDK

### 2. Android NDK

Download [Android NDK r21d](https://developer.android.com/ndk/downloads/older_releases) and extract it to a permanent location.

In Unity: **Edit → Preferences → External Tools**:
- Uncheck "NDK Installed with Unity"
- Set NDK path to your extracted `android-ndk-r21d` folder

### 3. Open the project

Open the project folder in Unity 2021.3.0f1.

### 4. Build

In Unity menu:

1. **Build → 0. Fix Android Tools** — sets SDK/NDK paths
2. **Build → 3. Create Scene + Build APK** — generates the scene and builds the APK

The APK is output to `Builds/AmblyoBye_PicoG2.apk`.

> The scene is built entirely in code (`SceneBuilderAndBuild.cs`). There is no manual scene editing — everything (UI, video player, materials, event system) is created programmatically.

### 5. Install on Pico G2

```bash
adb install -r Builds/AmblyoBye_PicoG2.apk
```

### 6. Add videos

```bash
adb push "MyVideo.mp4" "/sdcard/Android/data/com.amblyobye.amblyobye/files/Movies/"
```

Supported formats: `.asf`, `.avi`, `.dv`, `.m4v`, `.mp4`, `.mov`, `.mpg`, `.mpeg`, `.ogv`, `.vp8`, `.webm`, `.wmv`

> For best compatibility, convert videos to **MP4 (H.264, AAC audio)** using [HandBrake](https://handbrake.fr/) or similar tools.

---

## Architecture (for developers)

### Key files

| File | Description |
|------|-------------|
| `Assets/Editor/SceneBuilderAndBuild.cs` | Programmatic scene builder + APK build automation |
| `Assets/Resources/Scripts/DichopticMovieSceneManager.cs` | Main app logic, UI, video playback, settings |
| `Assets/Resources/Scripts/VRUISetup.cs` | VR camera + gaze reticle setup |
| `Assets/Resources/Scripts/PicoTouchpadClick.cs` | Input bridge (touchpad + headset body button → UI clicks) |
| `Assets/Resources/Scripts/PicoEyeIndexSetter.cs` | Sets `_PicoEyeIndex` per eye in `OnPreRender` |
| `Assets/Resources/Scripts/SliderPlusMinus.cs` | VR-friendly +/- button controls |
| `Assets/Resources/Scripts/DichopticMovieSettingsManager.cs` | Settings persistence to file |
| `Assets/Resources/Shaders/DichopticMovieUnlit.shader` | Dichoptic filter shader |

### Dichoptic shader

```hlsl
float blobVisible = lerp(noiseMask, 1.0 - noiseMask, eyeIndex);
```

- Eye 0 (left): sees blobs where noise > threshold
- Eye 1 (right): sees inverted pattern (noise <= threshold)
- `_PicoEyeIndex` is a global shader float set per-eye camera in `OnPreRender`

### Differences from the original

| | Original (Quest 2/3) | This port (Pico G2 4K) |
|---|---|---|
| Engine | Unity 6000.2.11f1 | Unity 2021.3.0f1 |
| VR SDK | OpenXR | Pico Legacy VR SDK |
| Tracking | 6DoF | 3DoF (head rotation only) |
| Input | Quest controllers (thumbstick, trigger) | Gaze + touchpad / headset body button |
| UI controls | Sliders | +/- buttons (gaze-friendly) |
| Eye rendering | XR stereo per-eye | `_PicoEyeIndex` via `OnPreRender` |
| Scene | Manual Unity editor scene | Fully code-generated |

---

## Therapy Guidelines

- Recommended: **1 hour/day, 6 days/week**
- Wear prescription glasses inside the headset
- Do NOT use while in a moving vehicle
- Consult your ophthalmologist for personalized recommendations

---

## Research Background

Sauvan, L., Stolowy, N., Denis, D., Matonti, F., Chavane, F., Hess, R. F., & Reynaud, A. (2019).
_Contribution of Short-Time Occlusion of the Amblyopic Eye to a Passive Dichoptic Video Treatment for Amblyopia beyond the Critical Period_.
Neural Plasticity, 2019. [DOI: 10.1155/2019/6208414](https://doi.org/10.1155/2019/6208414)

S. Boniquet-Sanchez, N. Sabater-Cruz (2019).
_Current Management of Amblyopia with New Technologies for Binocular Treatment_.
[DOI: 10.3390/vision5020031](https://doi.org/10.3390/vision5020031)

C. Lunghi, M. Berchicci, M. Concetta Morrone, F. Di Russo (2015).
_Short-term monocular deprivation alters early components of visual evoked potentials_.
[DOI: 10.1113/JP270950](https://doi.org/10.1113/JP270950)

---

## Disclaimer

AmblyoBye is intended for use under the guidance of a qualified eye care professional.
It is not a substitute for professional medical advice, diagnosis, or treatment.
Improving your vision while having strabismus might lead to diplopia. Please take that into consideration.

Always seek the advice of your physician or other qualified health provider with any questions you may have regarding a medical condition.

---

## License

This project is a fork of [AmblyoBye](https://github.com/alexmuraru27/AmblyoBye) by Alex Muraru. See original repository for license terms.

## Contact

- Original project: [amblyobye@gmail.com](mailto:amblyobye@gmail.com)
- Pico G2 port: [talipov.mars@gmail.com](mailto:talipov.mars@gmail.com) / [github.com/tmars/AmblyoBye](https://github.com/tmars/AmblyoBye)
