# CASE 20 전용 캠페인 설치 검증 계약

새 전용 캠페인을 등록할 때 ZIP 내부 폴더를 실제 게임 경로로 추측하지 않는다. **원작 런처와 임무가 요구하는 경로**를 먼저 확인하고, 배포 매니페스트와 별도의 독립 설치 계약(.layout.json)에 기록한다. 매니페스트를 그대로 복사해 계약을 만들면 검사가 의미 없다.

## 지원 형태

- 단일/다중 시작 맵, 여러 챕터 폴더, 하나의 시작 맵을 공유하는 에피소드
- 압축된 SC2Mod와 폴더형 SC2Mod 구성 파일
- ZIP 원본 경로와 게임 설치 목적지가 다른 명시적 파일 매핑

제작자 EXE, 외부 설치기, 특수 실행 흐름은 기존 외부 실행 연결로 분리한다. 일반 SC2Map 설치기로 추측 처리하지 않는다.

## 새 캠페인 등록 절차

1. 원작 설치 지침/맵 호출 경로에서 실제 필요한 Maps/Mods 위치를 확인한다. 확인할 수 없으면 공개 설치를 보류한다.
2. 배포 ZIP SHA-256, 각 파일 해시/크기, ZIP 원본 항목과 설치 목적지, 시작 맵을 명시한다.
3. 독립 .layout.json의 gameFiles, launchMaps, 필요하면 siblingRequirements를 작성한다.
4. 실제 ZIP을 격리 게임 폴더에 설치하는 공통 검사를 실행해 파일 해시/재설치/실행 경로/제거·기록 보존을 확인한다.
5. 기존 설치가 있다면 구버전 재설치·경로 이관, 수동 복사된 맵, 취소·충돌·실패 복원을 별도 검사한다.
6. 게임 안에서 시작 임무 및 다음 임무 전환까지 검증하지 않았다면 인게임 검증 완료라고 표시하지 않는다.

검사 명령 예시 (새 격리 결과 폴더를 마지막 인자로 넣는다):

    CampaignHangar.exe --dedicated-layout-audit "qa\contracts\odyssey-ko-20261114.layout.json" "release\odyssey-ko.json" "release\zips" "qa-results\new-run"

ZIP 폴더 내 파일명은 설치 목록 packages[].url 마지막 이름과 일치해야 한다. 검사 성공 시 생성된 layout-audit-result.json의 runtimeTriggersVerified=false는 인게임 미검증을 뜻한다.

원본 ZIP/공개 매니페스트/Bank/설치된 CCM/현재 실행 중인 EXE는 감사 중 변경하지 않는다.
