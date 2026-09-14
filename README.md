# Unity Screen Recorder (OBS Studio-Grade)

[![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/sjhdeux0114/UnityScreenRecoder/pulls)

유니티 에디터 상에서 OBS Studio 수준의 초고화질 무손실 게임 화면 및 시네마틱 영상을 녹화할 수 있는 고성능 Unity Package Manager (UPM) 패키지입니다.

기존 녹화 플러그인들의 화질 저하, 블록 노이즈, 프레임 드랍 문제를 해결하기 위해 **GPU 하드웨어 가속(NVENC, AMF, QSV)**과 **비동기 GPU 버퍼 추출(AsyncGPUReadback)**, **고정 프레임 렌더링 모드(Constant Framerate)**를 탑재했습니다.

---

## 주요 기능 (Features)

- **OBS Studio급 압도적인 화질 (GPU Hardware Acceleration)**:
  - NVIDIA NVENC, AMD AMF, Intel QSV 및 CPU libx264 지원
  - CQP 14~17 (무손실급 상수 품질 모드) 및 최대 200Mbps+ 비트레이트 지원으로 압축 노이즈 제거
  - H.264 및 HEVC (H.265) 코덱 지원
- **AsyncGPUReadback 기반 제로 스톨(Zero-Stall) 파이프라인**:
  - 기존의 `Texture2D.ReadPixels`로 인한 메인 스레드 멈춤(GPU stall)을 방지하고 백그라운드 스레드로 비동기 스트리밍
- **완벽 싱크 렌더링 모드 (Constant Framerate / 0% Dropped Frames)**:
  - `Time.captureFramerate`를 활성화하여 씬이 무겁거나 4K 녹화 시에도 **단 1프레임의 누락/끊김 없이 실크처럼 매끄러운 60fps/120fps 시네마틱 영상** 추출 (트레일러, 쇼케이스 제작에 최적)
- **게임 오디오 캡처 & 무손실 Remuxing**:
  - `AudioListener`의 출력을 실시간 캡처하여 녹화 종료 즉시 무손실 초고속 Remux로 오디오/비디오 완벽 결합
- **원클릭 FFmpeg 자동 다운로더 내장**:
  - 복잡한 FFmpeg 설치 과정 없이 에디터 창의 버튼 한 번으로 자동 다운로드 및 구성
- **간편한 에디터 인터페이스 & 단축키**:
  - 언제 어디서나 **`F9`** 키로 즉시 녹화 시작/정지 가능
  - 상단 메뉴 `Tools > High Quality Screen Recorder` (`Ctrl + Shift + R`)

---

## 설치 방법 (Installation)

### 1. Unity Package Manager (Git URL) - 권장

1. Unity 에디터를 열고 상단 메뉴 **Window > Package Manager**를 클릭합니다.
2. 좌측 상단의 **`+`** 버튼을 누르고 **`Add package from git URL...`**을 선택합니다.
3. 아래 Git URL을 입력하고 **Add**를 누릅니다:
   ```text
   https://github.com/sjhdeux0114/UnityScreenRecoder.git
   ```

### 2. manifest.json에 직접 추가

프로젝트의 `Packages/manifest.json` 파일에 다음 항목을 추가합니다:
```json
{
  "dependencies": {
    "com.studio.unityrecorder": "https://github.com/sjhdeux0114/UnityScreenRecoder.git",
    ...
  }
}
```

---

## 빠른 시작 (Quick Start)

1. **녹화 창 열기**:
   - Unity 상단 메뉴에서 **`Tools > High Quality Screen Recorder`** (단축키: `Ctrl + Shift + R`)를 엽니다.
2. **FFmpeg 엔진 상태 확인**:
   - 창 상단에 `Ready` 표시가 되어 있는지 확인합니다.
   - 처음 실행하여 FFmpeg이 없는 경우, **[Download & Setup FFmpeg Automatically]** 버튼을 누르면 자동으로 다운로드되어 즉시 준비됩니다.
3. **권장 녹화 설정**:
   - **Hardware Encoder**: `Auto` (NVIDIA 사용 시 자동으로 `NVENC` 활성화)
   - **Quality Preset**: `Ultra` (CQP 17 - OBS 권장 고화질) 또는 `Lossless` (CQP 14)
   - **Timing Mode**:
     - `Realtime`: 실시간 플레이 캡처 (일반 게임플레이 테스트)
     - `ConstantFramerate`: 렉 없이 완벽한 60fps 시네마틱 영상 추출 (쇼케이스/트레일러)
4. **녹화 시작 및 정지**:
   - 초록색 **[● START RECORDING (F9)]** 버튼을 누르거나 키보드 **`F9`**를 누릅니다.
   - 녹화를 마치려면 **`F9`**를 다시 누릅니다.
   - 녹화 완료 즉시 **[Open Folder]** 또는 **[Play Last Video]** 버튼으로 영상을 바로 확인하실 수 있습니다.

---

## 단축키 (Hotkeys)

| 단축키 | 기능 |
| :--- | :--- |
| **`F9`** | 녹화 시작 / 정지 토글 (Start / Stop Recording) |
| **`Ctrl + Shift + R`** | 녹화 설정 창 열기 (Open Recorder Window) |

---

## 시스템 요구 사양 (Requirements)

- **Unity**: Unity 2021.3 LTS 이상 (Built-in, Universal RP, High Definition RP 지원)
- **운영체제**: Windows 10 / 11 (64-bit)
- **GPU**: 
  - NVIDIA GeForce (GTX 10/16 시리즈, RTX 20/30/40 시리즈) - *NVENC 하드웨어 가속*
  - AMD Radeon (RX 시리즈) - *AMF 하드웨어 가속*
  - Intel Graphics - *QuickSync 하드웨어 가속*
  - (하드웨어 인코더가 없는 환경에서는 CPU x264로 자동 전환)

---

## 라이선스 (License)

이 프로젝트는 [MIT License](LICENSE.md) 하에 배포됩니다.
