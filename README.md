# High Quality Screen Recorder for Unity (OBS Studio-Grade)

유니티 에디터 상에서 OBS Studio 수준의 무손실/초고화질 게임 플레이 화면 및 영상을 직접 녹화할 수 있는 고성능 Unity Package Manager (UPM) 패키지입니다.

기존 녹화 플러그인의 화질 저하, 프레임 드랍, 블록 노이즈 문제를 해결하기 위해 **GPU 하드웨어 가속(NVENC, AMF, QSV)**과 **AsyncGPUReadback(논블로킹 GPU 버퍼 추출)**, **고정 프레임 모드(Time.captureFramerate)**를 적용했습니다.

---

## 주요 기능 및 특장점

1. **OBS Studio급 압도적인 화질 (NVIDIA NVENC / AMD AMF / Intel QSV / x264)**
   - GPU 하드웨어 인코더(NVENC, AMF, QSV)를 직접 연동하여 CPU 점유율을 거의 차지하지 않으면서 무손실에 준하는 화질(CQP 14~17, 100Mbps+)로 인코딩합니다.
   - H.264 및 HEVC (H.265) 코덱 지원.

2. **AsyncGPUReadback 기반 제로 스톨(Zero-Stall) 파이프라인**
   - 렌더 파이프라인을 멈추게 하는 구형 `Texture2D.ReadPixels` 대신, 비동기 GPU 메모리 직접 복사를 사용하여 플레이 중 버벅임을 방지합니다.

3. **고정 프레임 렌더링 모드 (Constant Framerate / 0% Dropped Frames)**
   - **실시간 모드 (Realtime)**: 실제 플레이하는 그대로 실시간 녹화
   - **완벽 싱크 렌더링 모드 (Constant Framerate)**: `Time.captureFramerate`를 동기화하여, 씬이 무겁거나 4K 녹화 중 에디터 프레임이 떨어져도 **단 1프레임의 누락/끊김 없이 실크처럼 매끄러운 60fps/120fps 시네마틱 영상**을 추출합니다. (트레일러, 쇼케이스 영상 제작에 최적)

4. **게임 오디오 완벽 캡처 & 초고속 Remuxing**
   - 게임의 `AudioListener` 출력을 실시간 수집하여, 녹화 정지 즉시 비디오와 오디오를 초고속 스트림 복사(Remux)로 무손실 결합합니다.

5. **원클릭 FFmpeg 자동 설정 도우미**
   - 별도로 복잡하게 FFmpeg을 설치하지 않아도, 에디터 창의 **[Download & Setup FFmpeg Automatically]** 버튼 클릭 한 번으로 자동 설치 및 즉시 사용이 가능합니다.

6. **원클릭 단축키 (F9)**
   - 에디터 상에서 언제든지 `F9` 키를 눌러 즉시 녹화를 시작하고 중지할 수 있습니다.

---

## 설치 및 적용 방법 (Unity Package Manager)

### 방법 1: Git URL로 추가 (권장)
1. 유니티 에디터 메뉴에서 **Window > Package Manager**를 엽니다.
2. 좌측 상단의 **`+`** 버튼을 누르고 **`Add package from git URL...`**을 선택합니다.
3. 이 레포지토리의 Git 주소를 입력합니다:
   ```text
   https://github.com/<your-username>/UnityRecoder.git
   ```
4. **Add**를 누르면 즉시 설치됩니다.

### 방법 2: 로컬 디스크에서 추가
1. **Window > Package Manager**를 엽니다.
2. 좌측 상단의 **`+`** 버튼을 누르고 **`Add package from disk...`**를 선택합니다.
3. 이 폴더의 `package.json` 파일을 선택합니다.

---

## 사용 가이드

1. **녹화 창 열기**:
   - 유니티 상단 메뉴에서 **`Tools > High Quality Screen Recorder`** (또는 단축키 `Ctrl + Shift + R`)를 클릭합니다.
2. **FFmpeg 확인**:
   - 창 상단의 **FFmpeg Engine Status**에 초록색 `Ready`가 표시되는지 확인합니다.
   - 만약 표시되지 않는다면, **[Download & Setup FFmpeg Automatically]** 버튼을 눌러 자동 설치합니다.
3. **화질 및 인코더 설정**:
   - **Hardware Encoder**: `Auto` (NVIDIA 그래픽카드 사용 시 자동으로 최적의 `NVENC` 선택)
   - **Quality Preset**:
     - `Ultra` (CQP 17): OBS 고화질 권장 세팅과 동일 (유튜브/포트폴리오 업로드 최적)
     - `Lossless` (CQP 14): 원본과 구별할 수 없는 최고급 무손실 화질
   - **Timing Mode**:
     - `Realtime`: 실시간 플레이 캡처
     - `ConstantFramerate`: 렉 없는 완벽한 60fps 시네마틱 트레일러 추출용
4. **녹화 시작 & 정지**:
   - 초록색 **[● START RECORDING (F9)]** 버튼을 누르거나 키보드 `F9`를 누릅니다.
   - 녹화 완료 후 **[■ STOP RECORDING (F9)]** 버튼을 누르면 1초 이내에 최종 고화질 MP4 파일이 생성됩니다.
   - 녹화 창의 **[Open Folder]** 및 **[Play Last Video]** 버튼을 통해 바로 확인할 수 있습니다.

---

## 요구 사양
- **Unity 버전**: Unity 2021.3 LTS 이상 (Built-in, URP, HDRP 모두 호환)
- **운영체제**: Windows 10 / 11 (64-bit)
- **권장 GPU**: NVIDIA GeForce (GTX 10/16 시리즈, RTX 20/30/40 시리즈) 또는 AMD Radeon, Intel GPU

---

## 라이선스
MIT License
