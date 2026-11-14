(function () {
  "use strict";
  // In live mode the existing native Hangar owns launch/install/update/ZIP
  // operations. This UI owns display, filtering and gesture interpolation.
  var THEMES = {
    terran: {
      concept: "A / 테란 작전 사령부", overline: "UNITED COMMAND", subline: "TACTICAL COMMAND BRIDGE",
      top: "TACTICAL OPERATIONS / C20", glyph: "⌬", radar: "⌖",
      title: "캠페인 아카이브", titleSide: "작전 통제실", eyebrow: "TACTICAL ARCHIVE / CAMPAIGN DIRECTORY",
      subtitle: "강철 장갑과 전술 HUD를 결합한 신속한 캠페인 관리 인터페이스",
      status: "TACTICAL UPLINK", bootKind: "COMMAND BRIDGE / SYSTEM START",
      bootDescription: "작전 사령부의 모든 모듈을 동기화하고 있습니다",
      bootSteps: ["발진 준비 시스템 점검", "캠페인 데이터 링크 확보", "전술 데이터베이스 연결", "작전 통제실 준비 완료"],
      kicker: "TACTICAL PRIORITY / SELECTED CAMPAIGN", ready: "작전 통제 시스템 온라인"
    },
    protoss: {
      concept: "B / 프로토스 워프 아카이브", overline: "WARP ARCHIVE", subline: "PSIONIC NEXUS",
      top: "WARP NEXUS / C20", glyph: "✧", radar: "◇",
      title: "워프 아카이브", titleSide: "빛의 기록실", eyebrow: "WARP ARCHIVE / PSIONIC INDEX",
      subtitle: "금빛 장식과 수정 에너지로 구성한 품격 있는 미래형 보관함",
      status: "WARP CORE STABLE", bootKind: "WARP NEXUS / ENERGY CHARGING",
      bootDescription: "수정 에너지의 흐름을 동기화하고 기록을 활성화합니다",
      bootSteps: ["워프 장치 정렬", "기록의 빛 연결", "고대 아카이브 공명", "워프 아카이브 활성화"],
      kicker: "WARP ARCHIVE / SELECTED CHRONICLE", ready: "워프 아카이브 활성화"
    },
    zerg: {
      concept: "C / 저그 군락 관제실", overline: "HIVE NETWORK", subline: "BROOD MEMORY NETWORK",
      top: "HIVE CONSCIOUSNESS / C20", glyph: "❖", radar: "✺",
      title: "군락 관제실", titleSide: "군체의 기억", eyebrow: "HIVE NETWORK / CAMPAIGN GENOME",
      subtitle: "보라색 생체 조직과 발광 신경망을 결합한 실험적 군락 UI",
      status: "HIVE SYNAPSE ACTIVE", bootKind: "HIVE NETWORK / AWAKENING",
      bootDescription: "군체의 신경망을 연결하고 캠페인 기억을 깨웁니다",
      bootSteps: ["생체 신호 감지", "군락 신경망 결합", "기억 조직 활성화", "군체 의식 연결 완료"],
      kicker: "HIVE GENOME / ACTIVE CAMPAIGN", ready: "군체 신경망 연결 완료"
    },
    nova: {
      concept: "D / 노바 고스트 특수작전", overline: "GHOST OPERATIONS", subline: "COVERT INTELLIGENCE CENTER",
      top: "GHOST PROGRAM / C20", glyph: "✧", radar: "⌖",
      title: "특수작전 관제실", titleSide: "NOVA / BLACK OPS",
      eyebrow: "CLASSIFIED / COVERT OPERATIONS",
      subtitle: "그림자 속에서 작전을 준비하는 스텔스·고스트 전술 인터페이스",
      status: "GHOST UPLINK SECURED", bootKind: "NOVA / COVERT CONNECTION",
      bootDescription: "기밀 전술 네트워크를 암호화하고 있습니다",
      bootSteps: ["암호화 채널 연결", "광학 위장 시스템 확인", "작전 기록 접근 승인", "고스트 작전실 준비 완료"],
      kicker: "CLASSIFIED OPERATION / SELECTED CAMPAIGN", ready: "스텔스 작전 네트워크 연결"
    }
  };
  var liveMode = new URLSearchParams(window.location.search).has("live");
  var channelLabel=new URLSearchParams(window.location.search).get("channel")==="release"?"CAMPAIGN HANGAR":"DEVELOPMENT";

  var campaigns = [
    {id:"revolite", name:"레볼라이트", en:"REVOLITE", kind:"로그라이트", category:"자날", installed:true, dedicated:false, author:"커뮤니티 제작", img:"assets/revolite.webp", desc:"자날 캠페인을 새로운 회차 성장과 서약, 특성 조합으로 다시 즐기는 로그라이트 모드. 다양한 유닛과 전술 선택을 시각적으로 비교합니다.", featured:true},
    {id:"dark", name:"다크 스토리", en:"DARK STORY", kind:"스토리 캠페인", category:"전용", installed:true, dedicated:true, author:"커뮤니티 번역", img:"assets/dark-story.webp", desc:"어둠 속에서 시작되는 장대한 이야기. 독립적인 캠페인 전개와 개성 있는 미션을 담은 한국어판 시연 항목입니다."},
    {id:"hotslegend", name:"군심 로그라이크 전설", en:"LEGEND OF THE SWARM", kind:"로그라이크", category:"군심", installed:true, dedicated:false, author:"커뮤니티 제작", img:"assets/hots-legend-cinematic.png", desc:"군단의 심장에서 전설 재능과 독특한 성장 방식을 활용해 저그 군단을 이끄는 캠페인입니다."},
    {id:"blackout", name:"블랙 아웃", en:"BLACKOUT", kind:"로그라이크", category:"자날", installed:true, dedicated:false, author:"커뮤니티 제작", img:"assets/blackout.webp", desc:"하이 파워 전투와 유닛 해금, 다양한 재능 조합으로 빠르게 변화하는 전장에 도전하는 모드입니다."},
    {id:"eskr", name:"ES + KR", en:"ENHANCED SWARM", kind:"확장 캠페인", category:"군심", installed:false, dedicated:false, author:"커뮤니티 제작", img:"assets/eskr.webp", desc:"확장된 저그 유닛과 강화된 전투를 중심으로 구성된 군단의 심장 변형 캠페인입니다."},
    {id:"cts", name:"CTS 신규 팩션", en:"CTS FACTIONS", kind:"진영 확장", category:"공유", installed:false, dedicated:false, author:"커뮤니티 제작", img:"assets/cts.webp", desc:"색다른 진영 선택과 영웅 조합으로 익숙한 캠페인을 완전히 새로운 플레이 방식으로 즐깁니다."},
    {id:"freedom", name:"자유의 사령관", en:"FREEDOM COMMANDERS", kind:"사령관 캠페인", category:"자날", installed:true, dedicated:false, author:"커뮤니티 제작", img:"assets/freedom.webp", desc:"자유의 날개 임무를 다양한 사령관 병력과 능력으로 새롭게 조합하는 캠페인입니다."},
    {id:"twoandtwo", name:"II & II · 디아&스타", en:"TWO & TWO", kind:"크로스오버", category:"공유", installed:false, dedicated:false, author:"커뮤니티 제작", img:"assets/two-and-two.webp", desc:"서로 다른 세계관의 전투 콘셉트를 혼합한 실험적인 캠페인 구성입니다."},
    {id:"borl", name:"블랙아웃 + 로그라이트", en:"BORL", kind:"로그라이트", category:"자날", installed:false, dedicated:false, author:"커뮤니티 제작", img:"assets/borl.webp", desc:"블랙아웃의 전투 시스템에 회차 성장과 숙련도를 결합한 변형 캠페인입니다."},
    {id:"odyssey", name:"오디세이", en:"ODYSSEY", kind:"스토리 캠페인", category:"전용", installed:false, dedicated:true, author:"커뮤니티 번역", img:"assets/dark-story.webp", desc:"긴 여정을 따라 진행되는 비CCM 커스텀 캠페인의 테스트용 목록 항목입니다."},
    {id:"ued", name:"UED 자날 한국어판", en:"UED LEGACY", kind:"한국어 번역", category:"자날", installed:false, dedicated:false, author:"커뮤니티 번역", img:"assets/cts.webp", desc:"UED 병력과 전술을 테마로 꾸민 자유의 날개 기반 커스텀 캠페인입니다."},
    {id:"nova", name:"노바 비밀 작전 확장", en:"NOVA OPERATIONS", kind:"임무 확장", category:"노바", installed:false, dedicated:false, author:"커뮤니티 제작", img:"assets/freedom.webp", desc:"노바 전용 작전의 은밀한 전투 분위기를 확장한 UI 시연용 항목입니다."}
  ];
  if (liveMode) campaigns = [];
  var state = {
    theme:"terran", selected:"revolite", view:"all", favorites:new Set(["revolite"]),
    favoritesOnly:false, search:"", category:"all", sort:"playtime", mode:"grid", contrast:false, reduceMotion:false
  };
  if (liveMode) state.favorites = new Set();
  var el = function (id) { return document.getElementById(id); };
  var bootFrame = null, toastTimer = null;
  var liveDataReceived = false, registeredCount = 12, installedOnly = false;
  var themeSwitchSerial=0,viewHistory=[];
  var hostWorkActive=false,hostWorkDisplayed=false,hostWorkSequence=-1,hostWorkId=0,hostWorkDismissedId=-1;
  // New installations start fullscreen. A user's saved *work-only* preference
  // changes future work displays, never race-switch or startup animations.
  var workBrowsing=false,workBrowseDefault=false;
  var workInitialRect=null;
  function syncDetachedWorkWindow(){
    if(!liveMode||!hostWorkActive||!workBrowsing||!workInitialRect)return;
    var style=getComputedStyle(document.body),colors={};
    ["bg","panel","panel2","text","dim","accent","accent-bright","accent-rgb","secondary","secondary-rgb",
      "edge","edge-faint","progressColor","buttonText"].forEach(function(key){
      colors["--"+key]=style.getPropertyValue("--"+key).trim();
    });
    sendHost("work-window-state:"+JSON.stringify({html:document.querySelector(".boot-center").outerHTML,
      theme:state.theme,colors:colors,rect:workInitialRect,workId:hostWorkId,
      indeterminate:el("boot").classList.contains("is-indeterminate")}));
  }
  function workWindowAttached(attached){
    el("boot").classList.toggle("is-native-work",!!attached&&hostWorkActive&&workBrowsing);
  }
  function dismissWorkWindow(){
    if(!hostWorkActive)return false;
    hostWorkDismissedId=hostWorkId;
    hostWorkDisplayed=false;
    el("boot").classList.remove("is-work","is-compact-work","is-native-work");
    el("boot").classList.add("is-hidden");
    el("bootWorkToolbar").hidden=true;
    document.body.classList.remove("is-booting","work-browsing");
    if(window.HangarFeatures)window.HangarFeatures.resumeArchive();
    toast("진행창을 닫았습니다. 작업은 계속 진행됩니다.");
    return true;
  }
  function showWorkBrowse(value){
    if(!value)hostWorkDismissedId=-1;
    workBrowsing=!!value;
    if(el("boot").classList.contains("is-compact-work")===workBrowsing&&
       document.body.classList.contains("is-booting")!==workBrowsing)return;
    if(workBrowsing){
      var rect=document.querySelector(".boot-center").getBoundingClientRect();
      workInitialRect={x:Math.round(rect.left),y:Math.round(rect.top),width:Math.round(rect.width),height:Math.round(rect.height),
        viewWidth:innerWidth,viewHeight:innerHeight};
      el("boot").style.setProperty("--work-box-left",rect.left+"px");
      el("boot").style.setProperty("--work-box-top",rect.top+"px");
      el("boot").style.setProperty("--work-box-width",rect.width+"px");
    }else{
      workWindowAttached(false);
      if(liveMode)sendHost("work-window-hide");
    }
    el("boot").classList.toggle("is-compact-work",workBrowsing);
    document.body.classList.toggle("work-browsing",workBrowsing);
    document.body.classList.toggle("is-booting",!workBrowsing);
    el("bootBrowse").textContent=workBrowsing?"크게 보기":"둘러보기";
    if(window.HangarFeatures){
      if(workBrowsing)window.HangarFeatures.resumeArchive();
      else window.HangarFeatures.suspendArchive();
    }
    syncDetachedWorkWindow();
  }
  function setWorkProgress(data) {
    if(!data || typeof data!=="object" || !Number.isFinite(Number(data.sequence)))return false;
    if(Number(data.sequence)<=hostWorkSequence)return false;
    if(data.workId!==undefined){
      var workId=Number(data.workId);
      if(!Number.isSafeInteger(workId)||workId<hostWorkId)return false;
      if(workId!==hostWorkId){
        workBrowsing=!!data.browse;
        hostWorkDismissedId=-1;
      }
      hostWorkId=workId;
    }
    hostWorkSequence=Number(data.sequence);
    hostWorkActive=!!data.active;
    if(!hostWorkActive){
      hostWorkDismissedId=-1;
      if(hostWorkDisplayed){
        hostWorkDisplayed=false;
        workWindowAttached(false);
        if(liveMode)sendHost("work-window-hide");
        el("boot").classList.remove("is-work","is-indeterminate");
        el("boot").classList.add("is-hidden");
        document.body.classList.remove("is-booting");
        el("bootWorkAmount").hidden=true;
        el("bootWorkOverall").hidden=true;
        el("bootWorkCancel").hidden=true;
        el("bootWorkToolbar").hidden=true;
        el("boot").classList.remove("is-compact-work");
        document.body.classList.remove("work-browsing");
        el("skipBoot").hidden=false;
        document.querySelector(".boot-headline").innerHTML='커캠 <span>격납고</span>';
        if(liveMode && window.HangarFeatures)window.HangarFeatures.resumeArchive();
      }
      return true;
    }
    if(hostWorkDismissedId===hostWorkId)return true;
    if(!data.fullscreen){
      if(hostWorkDisplayed){hostWorkDisplayed=false;el("boot").classList.add("is-hidden");
        el("boot").classList.remove("is-work","is-compact-work");
        el("bootWorkToolbar").hidden=true;
        document.body.classList.remove("is-booting","work-browsing");
        if(liveMode&&window.HangarFeatures)window.HangarFeatures.resumeArchive();}
      return true;
    }
    if(!hostWorkDisplayed){
      hostWorkDisplayed=true;
      workBrowsing=typeof data.browse==="boolean"?data.browse:workBrowseDefault;
      if(bootFrame!==null){cancelAnimationFrame(bootFrame);bootFrame=null;}
      if(window.HangarFeatures)window.HangarFeatures.suspendArchive();
      document.body.classList.add("is-booting");
      el("boot").classList.remove("is-hidden");
      el("boot").classList.add("is-work");
      el("skipBoot").hidden=true;
      el("bootWorkAmount").hidden=false;
      el("bootWorkOverall").hidden=false;
      el("bootWorkCancel").textContent="작업 중단";
    }
    el("bootWorkToolbar").hidden=false;
    showWorkBrowse(workBrowsing);
    var completed=Math.max(0,Number(data.completed)||0),total=Math.max(0,Number(data.total)||0);
    el("bootWorkCancel").hidden=!data.cancelable;
    el("bootWorkCancel").disabled=!data.cancelable;
    var hasTotal=total>0;
    var fraction=hasTotal?Math.min(100,Math.floor(completed*100/total)):null;
    el("boot").classList.toggle("is-indeterminate",!hasTotal);
    el("bootProgress").style.width=(hasTotal?fraction:100)+"%";
    setText("bootPercent",hasTotal?fraction+"%":"측정 중");
    setText("bootStep",String(data.stage||"작업 준비 중"));
    setText("bootKind",THEMES[state.theme].bootKind);
    setText("bootDescription",String(data.title||"캠페인 작업 중"));
    var unit=data.bytes?"MB":"개";
    var scaled=data.bytes?(completed/1048576).toFixed(1):Math.round(completed);
    var scaledTotal=data.bytes?(total/1048576).toFixed(1):Math.round(total);
    setText("bootWorkAmount",hasTotal?scaled+" / "+scaledTotal+" "+unit:
      (completed>0?scaled+" "+unit+" 처리 · 현재 단계 총량 미확정":"현재 단계 작업량 확인 중"));
    // Workloads mix network bytes, ZIP entries, hashing and UI commits:
    // those units cannot be added into a truthful overall percentage.
    setText("bootWorkOverall","현재 단계 "+(hasTotal?fraction+"%":"측정 중")+
      " · 전체 작업 진행 중 (총 소요량 미확정)");
    document.querySelector(".boot-headline").textContent="작업 진행 중";
    syncDetachedWorkWindow();
    return true;
  }
  var activationState={active:"",previous:""};
  function setActivationState(data){
    if(!data||typeof data!=="object")return false;
    activationState.activeId=String(data.activeId||"");
    activationState.previousId=String(data.previousId||"");
    activationState.versionKey=String(data.versionKey||"");
    activationState.active=String(data.activeName||"적용 내역 없음");
    activationState.previous=String(data.previousName||"기록 없음");
    setText("activeCampaignName",activationState.active);
    setText("previousCampaignName",activationState.previous);
    // No extra content row: identify state inside the existing cards,
    // large thumbnail and details instead.
    el("activationReadout").hidden=true;
    renderCards();
    refreshSelected();
    return true;
  }
  function appliedLabel(id){
    return id&&id===activationState.activeId?"현재 적용":
      id&&id===activationState.previousId?"직전 적용":"";
  }
  var CONTACT_SLOTS={wol:"자날",hots:"군심",lotv:"공유",nova:"노바",dedicated:"전용"};
  var lifetimeActivity={cleared:0,seconds:0};
  function setActivityTotals(data){
    if(!data)return false;
    lifetimeActivity={cleared:Math.max(0,Number(data.cleared)||0),seconds:Math.max(0,Number(data.seconds)||0)};
    el("statTotal").innerHTML=lifetimeActivity.cleared+"<span>개</span>";
    el("statPlayTime").innerHTML=(lifetimeActivity.seconds/3600).toFixed(1)+"<span>시간</span>";
    return true;
  }
  function setUpdateNotice(data){
    el("updateBanner").hidden=!data||!data.visible;
    if(data)setText("updateBannerVersion","v"+String(data.version||""));
    return true;
  }

  function safe(text) {
    return String(text).replace(/[&<>"']/g, function (x) {
      return ({"&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;"})[x];
    });
  }
  function sendHost(command) {
    try {
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(command);
    } catch (_) {}
  }
  // WebView2 owns the Windows file-drop HWND. Send the native File objects,
  // not browser-inaccessible paths or ZIP bytes, to the existing C# importer.
  function bindExternalZipDrop() {
    var overlay=el("zipDropOverlay"),depth=0;
    function files(e){return e.dataTransfer && Array.prototype.indexOf.call(e.dataTransfer.types||[],"Files")>=0;}
    function clear(){depth=0;overlay.hidden=true;}
    document.addEventListener("dragenter",function(e){if(!files(e))return;depth++;overlay.hidden=false;e.preventDefault();},true);
    document.addEventListener("dragover",function(e){if(!files(e))return;e.preventDefault();e.dataTransfer.dropEffect="copy";overlay.hidden=false;},true);
    document.addEventListener("dragleave",function(e){if(!files(e))return;if(--depth<=0)clear();},true);
    document.addEventListener("drop",function(e){
      if(!files(e))return;
      e.preventDefault();e.stopImmediatePropagation();clear();
      var dropped=Array.prototype.slice.call(e.dataTransfer.files||[]);
      if(!dropped.length||dropped.length>16){toast("ZIP 파일은 한 번에 1~16개 드롭해 주세요.");return;}
      if(!liveMode){toast("ZIP 드롭은 실제 개발판에서 설치 확인창을 엽니다.");return;}
      try{
        if(!window.chrome||!window.chrome.webview||!window.chrome.webview.postMessageWithAdditionalObjects)
          throw new Error("파일 드롭 연결을 사용할 수 없습니다.");
        window.chrome.webview.postMessageWithAdditionalObjects("import-drop",dropped);
        toast("드롭한 ZIP을 확인합니다. 설치 확인창에서 취소할 수도 있습니다.");
      }catch(_){toast("드롭을 전달하지 못했습니다. ZIP 가져오기 버튼을 사용해 주세요.");}
    },true);
  }
  function chosen() { return campaigns.find(function(c) { return c.id === state.selected; }) || null; }
  function setText(id, value) { el(id).textContent = value; }
  function safeCover(id,src){
    var target=el(id);if(!target)return;
    var serial=String((Number(target.dataset.coverSerial)||0)+1);
    target.dataset.coverSerial=serial;
    var fallback="assets/hangar.jpg";
    target.style.backgroundImage='url('+JSON.stringify(fallback)+')';
    if(!src||src===fallback)return;
    var preview=new Image();
    preview.onload=function(){if(target.dataset.coverSerial===serial)
      target.style.backgroundImage='url('+JSON.stringify(src)+')';};
    preview.onerror=function(){if(target.dataset.coverSerial===serial)
      target.style.backgroundImage='url('+JSON.stringify(fallback)+')';};
    preview.src=src;
  }
  function imagesFallback(scope){
    if(!scope)return;
    scope.querySelectorAll("img").forEach(function(img){
      img.addEventListener("error",function(){
        if(img.getAttribute("src")!=="assets/hangar.jpg")img.setAttribute("src","assets/hangar.jpg");
      },{once:true});
      if(img.complete&&img.naturalWidth===0&&img.getAttribute("src")!=="assets/hangar.jpg")
        img.setAttribute("src","assets/hangar.jpg");
    });
  }
  function renderContactGrid(){
    var grid=el("contactGrid");if(!grid)return;
    Object.keys(CONTACT_SLOTS).forEach(function(slot){
      var root=grid.querySelector('[data-contact-items="'+slot+'"]');if(!root)return;
      var list=campaigns.filter(function(c){return c.category===CONTACT_SLOTS[slot];});
      root.innerHTML=list.length?list.map(function(c){return '<button type="button" class="contact-campaign'+
        (c.id===state.selected?' is-selected':'')+'" data-contact-id="'+safe(c.id)+'" title="'+safe(c.name)+'">'+
        '<img src="'+safe(c.img)+'" alt="" loading="lazy"><span><strong>'+safe(c.name)+'</strong><small>'+
        safe(c.category+" · "+c.kind+" · "+c.author)+'</small><small>'+
        '플레이 '+(Number(c.playSeconds||0)/3600).toFixed(1)+'시간'+
        (c.bankLinked?' · 완료 '+Number(c.missions||0)+'개':'')+'</small></span></button>';}).join(""):
        '<p class="contact-empty">이 분류에 등록된 캠페인이 없습니다.</p>';
      imagesFallback(root);
    });
  }
  function setOptionLabel(id,value){
    var button=el(id),caption=button.querySelector("strong");
    if(caption)caption.textContent=value;else button.textContent=value;
  }
  function showNotificationCount(count){
    var n=el("sidebarNotificationCount"),value=Math.max(0,Number(count)||0);
    n.hidden=value===0;n.textContent=value>99?"99+":String(value);
    el("sideNotificationsButton").setAttribute("aria-label",value?"알림 열기 · 읽지 않음 "+value+"개":"알림 열기");
  }
  function toast(message) {
    setText("toastMessage", message);
    el("toast").hidden = false;
    if (toastTimer) window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(function () { el("toast").hidden = true; }, 3200);
  }
  var cardContextId="";
  function hideCardMenu(){cardContextId="";el("cardContextMenu").hidden=true;}
  function showCardMenu(id,x,y){
    if(!campaigns.some(function(c){return c.id===id;}))return;
    cardContextId=id;var menu=el("cardContextMenu");menu.hidden=false;
    var maxX=Math.max(8,window.innerWidth-menu.offsetWidth-10);
    var maxY=Math.max(8,window.innerHeight-menu.offsetHeight-10);
    menu.style.left=Math.min(maxX,Math.max(8,x))+"px";
    menu.style.top=Math.min(maxY,Math.max(8,y))+"px";
    menu.querySelector('[data-card-action="apply"]').focus();
  }
  function actionIcon(name){
    var paths={
      apply:'<path d="m12 3 8 4v6c0 4-8 8-8 8s-8-4-8-8V7Z"/><path d="m8 12 3 3 5-6"/>',
      install:'<path d="M12 3v12m-4-4 4 4 4-4M4 16v5h16v-5"/>',
      detail:'<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/>',
      camera:'<path d="m3 7 4 0 2-3h6l2 3h4v13H3Z"/><circle cx="12" cy="13" r="4"/>',
      star:'<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z"/>',
      zip:'<path d="M5 3h10l4 4v14H5Z"/><path d="M10 3v10m0 3v2m5-15v5h4"/>',
      back:'<path d="m10 5-7 7 7 7M3 12h18"/>',
      home:'<path d="m3 10 9-7 9 7M5 9v12h14V9M10 21v-7h4v7"/>',
      reload:'<path d="M20 8a8 8 0 1 0 0 8M20 3v5h-5"/>',
      external:'<path d="M14 3h7v7M21 3 10 14M10 5H3v16h16v-7"/>',
      remove:'<path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7"/>',
      versions:'<path d="M5 5h14v14H5Z"/><path d="M8 2h14v14M2 8v14h14"/>',
      folder:'<path d="M3 6h7l2 3h9v11H3Z"/><path d="M3 9h18"/>',
      quick:'<path d="m13 2-9 12h7l-1 8 10-13h-7Z"/>'
    };
    return '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">'+(paths[name]||paths.detail)+'</svg>';
  }
  function installActionIcons(){
    var names={launchButton:"apply",installButton:"install",detailButton:"detail",
      screenshotButton:"camera",importZipButton:"zip"};
    Object.keys(names).forEach(function(id){var node=el(id).querySelector(".action-icon");
      if(node)node.innerHTML=actionIcon(names[id]);});
    var rail=document.querySelector(".action-rail-label span");if(rail)rail.innerHTML=actionIcon("quick");
    ["back","home","reload","external"].forEach(function(name){
      var node=document.querySelector('[data-archive-action="'+name+'"]');
      if(node){node.setAttribute("aria-label",node.title||node.textContent);node.innerHTML=actionIcon(name);}
    });
    document.querySelectorAll("[data-manage-action]").forEach(function(node){
      var name=node.dataset.manageAction;
      node.innerHTML=actionIcon(name==="update"?"reload":name==="link"?"external":name)+
        "<span>"+safe(node.textContent)+"</span>";
    });
  }
  function installMenuIcons(){
    var paths={
      archive:'<path d="M3 5h7l2 2h9v12H3z"/><path d="M7 12h10M7 15h7"/>',
      all:'<rect x="3" y="4" width="8" height="7" rx="1"/><rect x="13" y="4" width="8" height="7" rx="1"/><rect x="3" y="13" width="8" height="7" rx="1"/><rect x="13" y="13" width="8" height="7" rx="1"/>',
      wol:'<path d="m12 3 2 5 5 1-4 4 1 5-4-2-4 2 1-5-4-4 5-1z" fill="currentColor" fill-opacity=".18"/><path d="M3 7v6l5 6m13-12v6l-5 6M2 17l5 4m15-4-5 4"/>',
      hots:'<path d="M12 2 9 8l3 8 3-8zM3 5l1 8 6 7-3-9zm18 0-4 6-3 9 6-7z" fill="currentColor" fill-opacity=".24"/><path d="m8 18 4 4 4-4M6 4 4 2m14 2 2-2"/>',
      lotv:'<path d="m12 2 5 8-5 12-5-12z" fill="currentColor" fill-opacity=".18"/><path d="M12 2v20M7 10h10M5 5l-3 7 4 6m13-13 3 7-4 6"/>',
      nova:'<circle cx="12" cy="12" r="7"/><path d="M12 1v6m0 10v6M1 12h6m10 0h6"/><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none"/>',
      dedicated:'<path d="M4 19V7l8-4 8 4v12l-8 3z"/><path d="M7 10l5-3 5 3-5 5zM12 15v6M4 19l8 2 8-2"/><path d="M12 7v4m-2-2h4"/>',
      custom:'<path d="M3 4h18v16H3zM9 4v16M9 11h12M15 11v9"/><circle cx="5" cy="7" r="1"/>',
      settings:'<path d="M3 6h18M3 12h18M3 18h18"/><circle cx="9" cy="6" r="2.3" fill="var(--panel2)"/><circle cx="16" cy="12" r="2.3" fill="var(--panel2)"/><circle cx="8" cy="18" r="2.3" fill="var(--panel2)"/>'
    };
    Object.keys(paths).forEach(function(view){
      var node=document.querySelector('.sidebar [data-view="'+view+'"] .nav-icon');
      if(!node)return;
      node.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">'+paths[view]+'</svg>';
    });
  }
  function getThemeFromQuery() {
    try {
      var theme = new URLSearchParams(window.location.search).get("theme");
      return Object.prototype.hasOwnProperty.call(THEMES, theme) ? theme : "terran";
    } catch (_) { return "terran"; }
  }
  function setTheme(theme, animate, fromHost, bootPrepared) {
    if (!THEMES[theme]) return false;
    var changing = state.theme !== theme;
    state.theme = theme;
    var t = THEMES[theme];
    document.body.dataset.theme = theme;
    if (liveMode && !fromHost) sendHost("theme:" + theme);
    setText("topProductCaption", t.top);
    var brandMark = document.querySelector('.theme-option[data-set-theme="'+theme+'"] .theme-emblem img');
    var brandGlyph = el("brandGlyph");
    if (brandGlyph && brandMark) brandGlyph.replaceChildren(brandMark.cloneNode(true));
    else setText("brandGlyph", t.glyph);
    setText("radarGlyph", t.radar);
    setText("brandOverline", t.overline);
    setText("brandSubline", t.subline);
    setText("statusTheme", t.status);
    setText("systemStatus", t.ready);
    if (brandMark) el("bootGlyph").replaceChildren(brandMark.cloneNode(true));
    else setText("bootGlyph", t.glyph);
    setText("bootKind", t.bootKind);
    setText("bootDescription", t.bootDescription);
    setText("heroKicker", t.kicker);
    document.querySelectorAll("[data-set-theme]").forEach(function (button) {
      var active = button.dataset.setTheme === theme;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    refreshHeading();
    refreshSelected();
    if (window.HangarFeatures) window.HangarFeatures.onTheme(theme);
    // Switching a skin also rebuilds the count label in the section heading.
    // Keep the badge synchronized with the cards currently on screen.
    renderCards();
    if (changing) {
      resetSmoothScroll(el("workspace"));
      resetSmoothScroll(el("campaignList").parentElement);
      el("workspace").scrollTop = 0;
      el("campaignList").parentElement.scrollTop = 0;
    }
    if (!bootPrepared&&(animate || (liveMode && changing && !fromHost)))
      replayBoot(liveMode?1000:undefined);
    sendHost("ready:" + t.concept);
    return true;
  }
  function switchThemeWithBoot(next){
    if(!THEMES[next]||state.theme===next)return false;
    if(!liveMode||state.reduceMotion||window.matchMedia("(prefers-reduced-motion: reduce)").matches)
      return setTheme(next,false,true);
    // Reveal the opaque loading layer BEFORE changing the underlying layout.
    // Two paint frames separate the click from the theme reflow.
    var serial=++themeSwitchSerial;
    el("bootProgress").style.width="0%";setText("bootPercent","00%");
    document.body.classList.add("is-booting");el("boot").classList.remove("is-hidden");
    if(window.HangarFeatures)window.HangarFeatures.suspendArchive();
    requestAnimationFrame(function(){requestAnimationFrame(function(){
      if(serial!==themeSwitchSerial)return;
      setTheme(next,false,false,true);replayBoot(1000);
    });});
    return true;
  }

  function refreshHeading() {
    var t = THEMES[state.theme], variant = {
      archive:"아카이브",
      all:"캠페인 목록",
      wol:"자날 캠페인",
      hots:"군심 캠페인",
      lotv:"공유 캠페인",
      nova:"노바 캠페인",
      installed:"캠페인 목록",
      dedicated:"전용 캠페인",
      favorites:"즐겨찾기 캠페인",
      settings:"환경 설정"
    };
    var title = variant[state.view] || t.title;
    el("workspaceTitle").innerHTML = safe(title) + " <span>" +
      safe(state.view === "all" ? t.titleSide : (liveMode ? "CAMPAIGN ARCHIVE" : "UI 테스트")) + "</span>";
    setText("sectionEyebrow", state.view === "all" ? t.eyebrow : "CAMPAIGN HANGAR / " + state.view.toUpperCase());
    setText("workspaceSubtitle", state.view === "all" ?
      (liveMode ? "실제 커캠 격납고에 등록된 캠페인을 확인하고 관리하는 작전실입니다." : t.subtitle) :
      state.view === "archive" ? "실제 커스텀 캠페인 아카이브 웹페이지입니다." :
      state.view === "settings" ? "테마, 시작 연출과 게임 설정을 관리할 수 있습니다." :
      (liveMode ? "실제 등록정보에서 해당 캠페인을 찾았습니다." :
        "표시되는 캠페인 정보는 UI 시연용 예시 데이터입니다."));
    var catalogCaption = state.favoritesOnly?"즐겨찾기 캠페인":
      (variant[state.view]||"캠페인 목록");
    el("catalogTitle").innerHTML = safe(catalogCaption) +
      ' <span id="resultCount">0</span>';
    var settings = state.view === "settings", archive = state.view === "archive";
    el("catalogPanel").hidden = settings || archive;
    el("settingsPanel").hidden = !settings;
    el("archiveEmbed").hidden = !archive;
    el("workspace").dataset.view = state.view;
    el("categoryFilter").hidden = state.view!=="all";
    document.querySelector(".workspace-header").hidden = archive;
    // An empty filter is not an empty library. Do not carry an unrelated
    // campaign preview/actions above the actual settings panel.
    el("hero").hidden = settings || archive;
    document.querySelector(".action-strip").hidden = settings || archive;
    document.querySelectorAll("[data-view]").forEach(function (button) {
      var active = button.dataset.view === state.view;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-current", active ? "page" : "false");
    });
  }
  function filtered() {
    var result = campaigns.filter(function (c) {
      var allowed = (!installedOnly||c.installed||c.dedicated) && (
        state.view === "installed" ? c.installed :
        state.view === "wol" ? c.category==="자날" :
        state.view === "hots" ? c.category==="군심" :
        state.view === "lotv" ? c.category==="공유" :
        state.view === "dedicated" ? c.category==="전용" :
        state.view === "nova" ? c.category==="노바" :
        state.view === "favorites" ? state.favorites.has(c.id) : true);
      if(state.favoritesOnly)allowed=allowed&&state.favorites.has(c.id);
      var matchCategory = state.view!=="all" || state.category === "all" || state.category === c.category;
      var search = state.search.trim().toLocaleLowerCase("ko");
      var matchText = !search ||
        (c.name + " " + c.en + " " + c.author + " " + c.kind + " " + c.category)
          .toLocaleLowerCase("ko").indexOf(search) >= 0;
      return allowed && matchCategory && matchText;
    });
    if (state.sort === "name") result.sort(function(a,b) { return a.name.localeCompare(b.name,"ko"); });
    else if (state.sort === "playtime") result.sort(function(a,b) {
      return (Number(b.playSeconds)||0)-(Number(a.playSeconds)||0) || a.name.localeCompare(b.name,"ko");
    });
    else if (state.sort === "popular") result.sort(function(a,b) {
      return (Number(b.recommendations)||0)-(Number(a.recommendations)||0) || a.name.localeCompare(b.name,"ko");
    });
    result.sort(function(a,b){
      var priority=function(c){return c.id===activationState.activeId?0:
        c.id===activationState.previousId?1:2;};
      return priority(a)-priority(b);
    });
    return result;
  }
  function cardMarkup(c, ordinal) {
    var selected = state.selected === c.id, favored = state.favorites.has(c.id);
    return '<button type="button" class="campaign-card' + (selected ? ' is-selected' : '') +
      '" data-card="' + safe(c.id) + '" aria-pressed="' + selected +
      '" aria-label="' + safe(c.name) + ' 캠페인 선택">' +
      '<span class="card-cover"><img loading="lazy" draggable="false" src="' + safe(c.img) + '" alt=""></span>' +
      '<span class="card-copy"><span class="card-topline"><span class="card-name">' + safe(c.name) +
      '</span>'+(appliedLabel(c.id)?'<span class="applied-state">'+appliedLabel(c.id)+'</span>':'')+'</span>'+
      (c.en && c.en.trim().toLocaleLowerCase("ko")!==c.name.trim().toLocaleLowerCase("ko")?
      '<span class="card-kind">'+safe(c.en)+'</span>':'') +
      '<span class="card-info">' + safe(c.category + " · " + c.kind + " · " + c.author) + '</span>' +
      (liveMode?'<span class="card-record">플레이 '+(Number(c.playSeconds||0)/3600).toFixed(1)+'시간 · '+
        (c.bankLinked?'완료 '+Number(c.missions||0)+'개':'완료 기록 미연결')+'</span>':'')+
      '<span class="card-endline">'+(liveMode&&!c.dedicated?'':'<span class="card-status' + (!c.installed ? ' not-installed' : '') + '">' +
      (c.installed ? (liveMode ? "설치됨" : "설치 예시") : (liveMode ? "미설치" : "미설치 예시")) +
      '</span>')+(c.updateState==="available"?'<span class="update-indicator card-update">업데이트</span>':'')+
      '<span class="card-favorite" title="이 캠페인 즐겨찾기 '+(favored?'해제':'추가')+'">' + (favored ? "★" : "☆") + '</span></span></span></button>';
  }
  function renderCards() {
    var items = filtered();
    // Filtered views must not keep advertising an unrelated campaign in
    // the large preview, detail sidebar, or execution/management buttons.
    // A newly empty search/favorites view has no actionable selection.
    if (state.view !== "settings" &&
        !items.some(function(c){return c.id === state.selected;}))
      state.selected = items.length ? items[0].id : null;
    if (liveMode) {
      // Species tabs show only their own installed campaigns; no global 127-card tab.
    }
    el("resultCount").textContent = String(items.length);
    el("campaignList").innerHTML = items.map(cardMarkup).join("");
    imagesFallback(el("campaignList"));
    el("campaignList").classList.toggle("as-list", state.mode === "list");
    el("emptyState").hidden = items.length > 0;
    setActivityTotals(lifetimeActivity);
    el("statInstalled").innerHTML = String(campaigns.filter(function(c) {return c.installed;}).length).padStart(2,"0") + "<span>개</span>";
    document.querySelectorAll("[data-card]").forEach(function (button) {
      button.addEventListener("click", function (event) {
        if(event.target.closest(".card-favorite")){toggleFavoriteFor(button.dataset.card);return;}
        selectCampaign(button.dataset.card);
      });
      button.addEventListener("contextmenu",function(event){
        event.preventDefault();event.stopPropagation();selectCampaign(button.dataset.card);
        showCardMenu(button.dataset.card,event.clientX,event.clientY);
      });
    });
    refreshSelected();
    renderContactGrid();
    return items.length;
  }
  function refreshSelected() {
    var c = chosen();
    ["launchButton","installButton","detailButton","recordDetails"]
      .forEach(function(id){el(id).disabled = !c;});
    el("favoriteButton").disabled=false;
    el("favoriteButton").setAttribute("aria-pressed",String(state.favoritesOnly));
    el("favoriteButton").innerHTML=actionIcon("star");
    el("favoriteButton").title=state.favoritesOnly?"즐겨찾기 필터 해제":"즐겨찾기만 보기";
    el("favoriteButton").setAttribute("aria-label",el("favoriteButton").title);
    if (!c) {
      var hasEntries = campaigns.length > 0;
      setText("heroTitle", hasEntries ? "조건에 맞는 캠페인이 없습니다" : "등록된 캠페인이 없습니다");
      setText("heroDescription", hasEntries ? "검색어나 분류를 변경해 보세요." :
        "내 캠페인에서 ZIP을 가져오거나 아카이브에서 캠페인을 확인해 보세요.");
      setText("heroTag", "CAMPAIGN STORAGE / EMPTY");
      setText("heroCategory", hasEntries ? "검색 결과 없음" : "실제 보관함 연결");
      setText("heroState", hasEntries ? "조건 불일치" : "등록 대기");
      setText("heroIndex", "00—00");
      setText("detailOrdinal", "00 / 00");
      setText("detailTitle", hasEntries ? "조건에 맞는 캠페인이 없습니다" : "등록할 캠페인을 선택하세요");
      setText("detailDescription", hasEntries ? "검색어나 분류를 변경해 보세요." :
        "이 화면은 실제 커캠 격납고의 보관함을 읽어 표시합니다.");
      setText("detailAuthor", "—");
      setText("detailType", "—");
      setText("detailCategory", "CAMPAIGN STORAGE / EMPTY");
      setText("detailInstalled", "없음");
      setText("recordPlayTime","—");setText("recordMissions","—");
      el("heroPlayTime").querySelector("strong").textContent="기록 없음";
      ["focusAuthor","focusCategory","focusInstalled","focusMissions","contactAuthor","contactType","contactPlayTime"]
        .forEach(function(id){setText(id,"—");});
      setText("contactTitle","캠페인을 선택하세요");setText("contactDescription","목록에서 캠페인을 선택하면 상세 정보가 표시됩니다.");
      el("heroUpdateBadge").hidden=true;el("detailUpdateBadge").hidden=true;
      safeCover("heroImage",null);safeCover("detailArt",null);safeCover("contactArt",null);
      setText("statusMessage", hasEntries ? "조건에 맞는 캠페인이 없습니다." : "등록된 캠페인이 없습니다.");
      return;
    }
    var dedicated=!!c.dedicated;
    var applied=!dedicated&&c.id===activationState.activeId&&
      c.versionKey&&c.versionKey===activationState.versionKey;
    el("launchButton").disabled=!!applied;
    if(dedicated&&!c.installed)el("launchButton").disabled=true;
    setText("primaryActionText",dedicated?"캠페인 실행":applied?"적용됨":"캠페인 적용");
    setText("heroTitle", c.name);
    setText("heroDescription", c.desc);
    setText("heroTag",appliedLabel(c.id) || (liveMode&&!dedicated?"선택한 캠페인":c.installed ?
      (liveMode ? "선택한 작전 / 설치됨" : "선택한 작전 / 설치 예시") :
      (liveMode ? "신규 작전 / 미설치" : "신규 작전 / 미설치 예시")));
    setText("heroCategory", c.category + " / " + c.kind);
    setText("heroState", c.installed ? (liveMode ? "설치됨" : "설치 예시") :
      (liveMode ? "미설치" : "미설치 예시"));
    el("heroState").hidden=liveMode&&!dedicated;
    var index = campaigns.findIndex(function(x) {return x.id === c.id;})+1;
    setText("heroIndex", ("0" + index).slice(-2) + "—" + campaigns.length);
    setText("detailOrdinal", ("0" + index).slice(-2) + " / " + campaigns.length);
    safeCover("heroImage",c.img);safeCover("detailArt",c.img);safeCover("contactArt",c.img);
    setText("detailTitle", c.name);
    setText("detailDescription", c.desc);
    setText("detailCategory",(appliedLabel(c.id)?appliedLabel(c.id)+" · ":"") + c.category + " / " + c.kind);
    setText("detailAuthor", c.author);
    setText("detailType", c.category + " · " + c.kind);
    setText("detailInstalled", c.installed ? (liveMode ? "설치됨" : "설치 예시") :
      (liveMode ? "미설치" : "미설치 예시"));
    el("detailInstalled").parentElement.hidden=liveMode&&!dedicated;
    setText("recordPlayTime",typeof c.playSeconds==="number"?(c.playSeconds/3600).toFixed(1)+"시간":"기록 없음");
    el("heroPlayTime").querySelector("strong").textContent=c.playSeconds>0?(c.playSeconds/3600).toFixed(1)+"시간":"기록 없음";
    el("heroUpdateBadge").hidden=c.updateState!=="available";
    el("detailUpdateBadge").hidden=c.updateState!=="available";
    setText("recordMissions",c.bankLinked?(Number(c.missions||0)+"개") : "미연결");
    setText("focusAuthor",c.author);setText("focusCategory",c.category+" · "+c.kind);
    setText("focusInstalled",c.installed?"설치됨":"미설치");
    el("focusInstalled").parentElement.hidden=liveMode&&!dedicated;
    setText("focusMissions",c.bankLinked?(Number(c.missions||0)+"개"):"미연결");
    setText("contactTitle",c.name);setText("contactDescription",c.desc);
    setText("contactAuthor",c.author);setText("contactType",c.category+" · "+c.kind);
    setText("contactPlayTime",typeof c.playSeconds==="number"?(c.playSeconds/3600).toFixed(1)+"시간":"기록 없음");
    setText("statusMessage", c.name + (liveMode ? " 선택됨 · 실제 캠페인 자료" : " 선택됨 · 디자인 테스트 모드"));
  }
  function selectCampaign(id) {
    if (!campaigns.some(function(c) { return c.id === id; })) return false;
    if(state.selected===id)return true;
    state.selected = id;
    // Selecting an installed card must not tear down and rebuild every
    // thumbnail, image loader, event handler and scroll surface.
    document.querySelectorAll("#campaignList [data-card]").forEach(function(node){
      var selected=node.dataset.card===id;
      node.classList.toggle("is-selected",selected);
      node.setAttribute("aria-pressed",String(selected));
    });
    refreshSelected();
    if(!el("contactGrid").hidden)renderContactGrid();
    if (window.HangarFeatures) window.HangarFeatures.onSelection(chosen());
    return true;
  }
  function revealCampaign(id,category){
    if(!campaigns.some(function(c){return c.id===id;}))return false;
    var views={"자날":"wol","군심":"hots","공유":"lotv","노바":"nova","전용":"dedicated"};
    var match=campaigns.find(function(c){return c.id===id;});
    var target=views[category]||views[match.category]||"all";
    setView(target,true);
    // The saved menu visibility is a layout preference, not an import veto.
    if(!filtered().some(function(c){return c.id===id;}))setView("all",true);
    if(!selectCampaign(id))return false;
    var card=Array.from(document.querySelectorAll("#campaignList [data-card]"))
      .find(function(n){return n.dataset.card===id;});
    if(!card)return false;
    card.scrollIntoView({block:"center",behavior:"auto"});
    // safeCover places the verified local fallback immediately; replacing it
    // with a decoded campaign cover is asynchronous and never exposes blank UI.
    return state.selected===id && card.getAttribute("aria-pressed")==="true" &&
      el("heroTitle").textContent===match.name &&
      el("detailTitle").textContent===match.name &&
      !!el("heroImage").style.backgroundImage &&
      !!el("detailArt").style.backgroundImage;
  }
  function setView(view, forceReset, fromHistory) {
    if(view==="custom"){
      if(window.HangarFeatures)window.HangarFeatures.beginEdit(null);
      return true;
    }
    if (["archive", "all", "installed", "wol", "hots", "lotv", "nova", "dedicated", "favorites", "settings"].indexOf(view) < 0) return false;
    hideCardMenu();
    var changed = state.view !== view;
    if(changed&&!fromHistory){viewHistory.push(state.view);if(viewHistory.length>24)viewHistory.shift();}
    state.view = view;
    if(changed||forceReset)state.favoritesOnly=false;
    if ((changed || forceReset) && view !== "settings") {
      // Each section starts with its first visible card. Otherwise the
      // preview may refer to an offscreen selection from another section.
      // Search/category are section-local rather than silent global filters.
      state.selected = null;
      state.search = "";
      state.category = "all";
      el("campaignSearch").value = "";
      el("campaignCategory").value = "all";
    }
    refreshHeading();
    if (view === "archive" || view === "settings") {
      window.__hangarViewTiming={view:view,domMs:performance.now()};
      if (window.HangarFeatures) window.HangarFeatures.onView(view);
      if (view === "settings" && !liveMode) toast("설정은 UI 테스트 앱에만 적용됩니다.");
      return true;
    }
    var listRegion = el("campaignList").parentElement;
    resetSmoothScroll(listRegion);
    listRegion.scrollTop = 0;
    resetSmoothScroll(el("workspace"));
    el("workspace").scrollTop = 0;
    renderCards();
    if (window.HangarFeatures) window.HangarFeatures.onView(view);
    if (view === "settings" && !liveMode) toast("설정은 UI 테스트 앱에만 적용됩니다.");
    return true;
  }
  function navigateBack(){
    if(window.HangarFeatures&&window.HangarFeatures.handleBack&&window.HangarFeatures.handleBack())return true;
    if(state.view==="archive"){
      if(liveMode)sendHost("archive-back");else setView("all",true,true);
      return true;
    }
    var before=viewHistory.pop();
    return setView(before&&before!==state.view?before:"all",true,true);
  }
  function toggleFavoriteFor(id) {
    var selected = campaigns.find(function(c){return c.id===id;});
    if (!selected) { toast("등록된 캠페인이 없습니다."); return; }
    if (state.favorites.has(id)) state.favorites.delete(id);
    else state.favorites.add(id);
    renderCards();
    if (liveMode) sendHost("favorites:" + JSON.stringify(Array.from(state.favorites)));
    // Removing the final favorite empties this view and clears selection.
    // Keep the clicked item's name instead of dereferencing chosen() again.
    toast(selected.name + (state.favorites.has(id) ? " · 즐겨찾기에 추가했습니다." : " · 즐겨찾기를 해제했습니다."));
  }
  function toggleFavorite(){var selected=chosen();if(selected)toggleFavoriteFor(selected.id);}
  function toggleFavoritesFilter(){
    if(state.view==="archive"||state.view==="settings")setView("all");
    state.favoritesOnly=!state.favoritesOnly;
    resetSmoothScroll(el("campaignList").parentElement);
    el("campaignList").parentElement.scrollTop=0;
    refreshHeading();renderCards();
  }
  function setMode(mode) {
    state.mode = mode === "list" ? "list" : "grid";
    el("gridMode").classList.toggle("is-active", state.mode === "grid");
    el("listMode").classList.toggle("is-active", state.mode === "list");
    el("gridMode").setAttribute("aria-pressed", String(state.mode === "grid"));
    el("listMode").setAttribute("aria-pressed", String(state.mode === "list"));
    el("campaignList").classList.toggle("as-list", state.mode === "list");
  }

  // Constant-distance, frame-rate-independent wheel interpolation for the
  // real archive. A physical wheel notch moves precisely 2 x 3 lines; fast
  // successive notches retarget the same animation instead of queuing up
  // separate 200ms animations. Native scrollbar thumbs remain freely
  // draggable without smoothing or delayed snap-back.
  var smooth = new Map();
  function scrollState(element) {
    if (!smooth.has(element))
      smooth.set(element, {frame:0,target:element.scrollTop,last:0,startedAt:0});
    return smooth.get(element);
  }
  function resetSmoothScroll(element) {
    if (!element) return;
    var s = scrollState(element);
    if (s.frame) cancelAnimationFrame(s.frame);
    s.frame = 0; s.last = 0; s.startedAt = 0;
    s.target = element.scrollTop;
  }
  function wheelDistance(event, element) {
    var font = Number.parseFloat(getComputedStyle(document.body).fontSize) || 16;
    var base = font * 3; // the original three-line wheel step, in CSS px
    if (event.deltaMode === 1) return event.deltaY * font * 2;
    if (event.deltaMode === 2) return event.deltaY * element.clientHeight;
    // High-resolution touchpads emit a stream of fractional pixels. Preserve
    // those small deltas; only discrete mouse-wheel notches get the 2x step.
    if (Math.abs(event.deltaY) < 38) return event.deltaY;
    return event.deltaY / 100 * base * 2;
  }
  function canMove(element, delta) {
    var max = element.scrollHeight - element.clientHeight;
    return max > 1 && (delta < 0 ? element.scrollTop > 1 : element.scrollTop < max-1);
  }
  function driveScroll(element, delta) {
    var s = scrollState(element);
    var max = Math.max(0,element.scrollHeight - element.clientHeight);
    if (!s.frame) s.target = element.scrollTop;
    var before=s.target;
    s.target = Math.min(max,Math.max(0,s.target + delta));
    var remaining=delta-(s.target-before);
    if (Math.abs(s.target-element.scrollTop) < .45) return {handled:false,remaining:delta};
    if (!s.frame) {
      s.last = 0;
      s.startedAt = performance.now();
      function step(now) {
        // Keep elapsed wall time even if Chromium throttles the first frame.
        // Do not queue delayed 16ms steps after a slow render / tab switch.
        var dt = s.last ? Math.min(400,Math.max(1,now-s.last)) :
          Math.min(400,Math.max(16,now-s.startedAt));
        s.last = now;
        var gap = s.target-element.scrollTop;
        if (Math.abs(gap) <= .6) {
          element.scrollTop=s.target;
          s.frame=0;s.last=0;
          return;
        }
        // 66ms time constant: visible, quick movement with no delayed queue.
        var fraction = 1-Math.exp(-dt/66);
        element.scrollTop += gap*fraction;
        s.frame=requestAnimationFrame(step);
      }
      s.frame=requestAnimationFrame(step);
    }
    return {handled:true,remaining:remaining};
  }
  function installSmoothWheel(element) {
    if (!element) return;
    scrollState(element);
    element.addEventListener("wheel",function(event) {
      if (event.ctrlKey || state.reduceMotion ||
          window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
      var delta=wheelDistance(event,element);
      if(delta===0)return;
      if(!canMove(element,delta))resetSmoothScroll(element);
      var movement=driveScroll(element,delta);
      if(element.matches(".contact-items,.contact-selected")&&movement.remaining!==0){
        var outer=el("workspace");
        if(canMove(outer,movement.remaining)){
          driveScroll(outer,movement.remaining);
          movement.handled=true;
        }
      }
      if(movement.handled){event.preventDefault();event.stopPropagation();}
    },{passive:false});
    element.addEventListener("pointerdown",function(event) {
      if(event.button===0) resetSmoothScroll(element);
    },{passive:true});
    // If the scrollbar is dragged, reset the wheel target to the user's
    // actual scrollbar position; never interpolate a thumb drag.
    element.addEventListener("pointermove",function(event) {
      if(event.buttons & 1) resetSmoothScroll(element);
    },{passive:true});
  }
  function applyLivePreferences(prefs) {
    if (!prefs || typeof prefs !== "object") return false;
    if (typeof prefs.browseWhileWorking==="boolean") {
      workBrowseDefault=prefs.browseWhileWorking;
      if(hostWorkDisplayed)showWorkBrowse(workBrowseDefault);
    }
    setOptionLabel("workBrowseToggle",workBrowseDefault?"작업 진행: 진행 박스만":"작업 진행: 전체 화면");
    el("workBrowseToggle").setAttribute("aria-pressed",String(workBrowseDefault));
    if (typeof prefs.reducedMotion==="boolean") state.reduceMotion=prefs.reducedMotion;
    if (typeof prefs.highContrast==="boolean") state.contrast=prefs.highContrast;
    document.body.classList.toggle("reduce-motion",state.reduceMotion);
    document.body.classList.toggle("high-contrast",state.contrast);
    var motion=el("motionToggle"),contrast=el("contrastToggle");
    setOptionLabel("motionToggle",state.reduceMotion?"모션 기본값":"모션 줄이기");
    motion.setAttribute("aria-pressed",String(state.reduceMotion));
    setOptionLabel("contrastToggle",state.contrast?"고대비 끄기":"고대비 켜기");
    contrast.setAttribute("aria-pressed",String(state.contrast));
    if (prefs.theme && THEMES[prefs.theme] && state.theme!==prefs.theme)
      setTheme(prefs.theme,false,true);
    return true;
  }

  function finishBoot() {
    if(hostWorkActive)return;
    if (bootFrame !== null) { cancelAnimationFrame(bootFrame); bootFrame = null; }
    el("bootProgress").style.width = "100%";
    setText("bootPercent", "100%");
    el("boot").classList.add("is-hidden");
    document.body.classList.remove("is-booting");
    if (liveMode && window.HangarFeatures) window.HangarFeatures.resumeArchive();
  }
  function replayBoot(durationOverride) {
    if(hostWorkActive)return;
    if (bootFrame !== null) { cancelAnimationFrame(bootFrame); bootFrame = null; }
    if (liveMode && window.HangarFeatures) window.HangarFeatures.suspendArchive();
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches || state.reduceMotion) {
      finishBoot(); return;
    }
    var config = THEMES[state.theme], start = performance.now();
    var duration = typeof durationOverride==="number"?durationOverride:
      (liveMode?240:(state.theme === "terran" ? 2360 : state.theme === "protoss" ? 2750 : 2570));
    document.body.classList.add("is-booting");
    el("boot").classList.remove("is-hidden");
    el("bootProgress").style.width = "0%";
    setText("bootPercent", "00%");
    function frame(now) {
      var ratio = Math.max(0, Math.min(1, (now-start)/duration));
      var percent = Math.floor(ratio * 100);
      el("bootProgress").style.width = percent + "%";
      setText("bootPercent", ("0" + percent).slice(-2) + "%");
      setText("bootStep", config.bootSteps[Math.min(3, Math.floor(ratio*4))]);
      if (ratio >= 1) { bootFrame = null; window.setTimeout(finishBoot, liveMode?0:170); }
      else bootFrame = requestAnimationFrame(frame);
    }
    bootFrame = requestAnimationFrame(frame);
  }
  function updateClock() {
    try {
      setText("clock", new Date().toLocaleTimeString("ko-KR", {hour:"2-digit",minute:"2-digit",hour12:false}));
    } catch (_) { setText("clock", "--:--"); }
  }
  function bindEvents() {
    el("bootWorkCancel").addEventListener("click",function(){
      if(!hostWorkActive || this.disabled)return;
      this.disabled=true;this.textContent="중단 요청 중";
      sendHost("work-cancel");
    });
    installMenuIcons();
    bindExternalZipDrop();
    // Includes thumbnails injected by the site-link chooser after the main
    // campaign cards were already rendered. No broken-image icon survives.
    document.addEventListener("error",function(e){
      var img=e.target;
      if(img&&img.tagName==="IMG"&&!img.dataset.imageFallback){
        img.dataset.imageFallback="true";img.src="assets/hangar.jpg";
      }
    },true);
    document.querySelectorAll("[data-card-action]").forEach(function(button){
      button.addEventListener("click",function(){
        var id=cardContextId,action=button.dataset.cardAction;hideCardMenu();
        if(!id)return;
        if(liveMode)sendHost("manage-action:"+action+":"+id);
        else toast("컨텍스트 메뉴는 실제 개발판에서 작동합니다.");
      });
    });
    document.addEventListener("pointerdown",function(e){if(!e.target.closest("#cardContextMenu"))hideCardMenu();});
    document.querySelectorAll("[data-view]").forEach(function (b) {
      b.addEventListener("click", function () { setView(b.dataset.view); });
    });
    document.querySelectorAll("[data-set-theme]").forEach(function (b) {
      b.addEventListener("click", function () { switchThemeWithBoot(b.dataset.setTheme); });
    });
    el("contactGrid").addEventListener("click",function(e){
      var button=e.target.closest("[data-contact-id]");
      if(button){selectCampaign(button.dataset.contactId);return;}
    });
    // Mouse XBUTTON1 (back/side button). WebView2 receives the gesture even
    // when the WinForms owner cannot see child-HWND mouse messages.
    var lastMouseBack=-1000;
    function mouseBack(e){
      if(e.button!==3)return;
      e.preventDefault();e.stopPropagation();
      if(performance.now()-lastMouseBack<320)return;
      lastMouseBack=performance.now();navigateBack();
    }
    document.addEventListener("auxclick",mouseBack,true);
    document.addEventListener("mouseup",mouseBack,true);
    el("sideYoutubeButton").addEventListener("click",function(){
      if(liveMode)sendHost("open-youtube");
      else window.open("https://www.youtube.com/@deadsignalcinema","_blank","noopener");
    });
    el("sideNotificationsButton").addEventListener("click",function(){
      if(liveMode)sendHost("open-notifications");
      else toast("알림 목록은 실제 개발판에서 열 수 있습니다.");
    });
    el("sideSupportButton").addEventListener("click",function(){
      setView("archive");
      if(liveMode)sendHost("archive-support");
      else toast("모더·후원 탭은 실제 개발판 아카이브에서 열립니다.");
    });
    el("optimizedMode").addEventListener("click",function () {
      if (liveMode) sendHost("theme:classic");
      else toast("간편 UI는 실제 커캠 격납고 개발판에서 사용할 수 있습니다.");
    });
    installSmoothWheel(el("workspace"));
    installSmoothWheel(el("campaignList").parentElement);
    document.querySelectorAll(".contact-items,.contact-selected").forEach(installSmoothWheel);
    el("campaignSearch").addEventListener("input", function (e) {
      state.search = e.target.value; resetSmoothScroll(el("campaignList").parentElement);
      el("campaignList").parentElement.scrollTop=0;renderCards();
    });
    el("campaignCategory").addEventListener("change", function (e) {
      state.category=e.target.value; resetSmoothScroll(el("campaignList").parentElement);
      el("campaignList").parentElement.scrollTop=0;renderCards();
    });
    el("sortCampaigns").addEventListener("change", function (e) {
      if(e.target.value==="popular" && !campaigns.some(function(c){return Number.isFinite(c.recommendations);})){ 
        e.target.value=state.sort;
        toast("아카이브 추천 수가 연결되지 않아 인기순을 사용할 수 없습니다.");return;
      }
      state.sort=e.target.value;renderCards();
    });
    el("gridMode").addEventListener("click", function () { setMode("grid"); });
    el("listMode").addEventListener("click", function () { setMode("list"); });
    el("clearSearch").addEventListener("click", function () {
      state.search=""; state.category="all";
      el("campaignSearch").value=""; el("campaignCategory").value="all";
      resetSmoothScroll(el("campaignList").parentElement);
      el("campaignList").parentElement.scrollTop=0;
      renderCards();
    });
    el("importZipButton").addEventListener("click",function(){
      if(liveMode) sendHost("import-zip");
      else toast("ZIP 가져오기는 실제 개발판에서 사용할 수 있습니다.");
    });
    el("favoriteButton").addEventListener("click", toggleFavoritesFilter);
    el("screenshotButton").addEventListener("click",function(){
      if(liveMode)sendHost("open-screenshots");
      else toast("실제 개발판에서 스타2 스크린샷 폴더를 엽니다.");
    });
    el("updateBannerInstall").addEventListener("click",function(){if(liveMode)sendHost("app-update-install");});
    el("updateBannerIgnore").addEventListener("click",function(){if(liveMode)sendHost("app-update-ignore");});
    el("recordDetails").addEventListener("click",function(){
      if(chosen()&&window.HangarFeatures)window.HangarFeatures.openRecord(chosen());
    });
    el("launchButton").addEventListener("click", function () {
      if (liveMode) {
        if (!chosen()) { toast("등록된 캠페인이 없습니다."); return; }
        sendHost("play:" + chosen().id);
        return;
      }
      toast("UI 테스트본입니다. 실제 게임은 실행하지 않습니다.");
    });
    el("installButton").addEventListener("click", function () {
      if (liveMode) {
        if (!chosen()) { toast("관리할 캠페인을 먼저 선택해 주세요."); return; }
        if (window.HangarFeatures) window.HangarFeatures.openManage(chosen());
        return;
      }
      toast("UI 테스트본입니다. 캠페인을 설치하거나 삭제하지 않습니다.");
    });
    el("detailButton").addEventListener("click", function () {
    if (liveMode && chosen()) {
        var id=chosen().id;setView("archive");sendHost("archive-detail:"+id);
        return;
      }
      var panel=el("detailPanel");
      if (getComputedStyle(panel).display === "none")
        toast("선택 캠페인: " + chosen().name + " · " + chosen().kind);
      else {
        panel.animate([{filter:"brightness(1.8)"},{filter:"brightness(1)"}],{duration:450,iterations:1});
        panel.scrollIntoView({block:"nearest",behavior:"smooth"});
      }
    });
    el("contrastToggle").addEventListener("click", function () {
      state.contrast = !state.contrast;
      document.body.classList.toggle("high-contrast",state.contrast);
      this.setAttribute("aria-pressed",String(state.contrast));
      setOptionLabel("contrastToggle",state.contrast?"고대비 끄기":"고대비 켜기");
      if(liveMode) sendHost("settings:contrast:"+(state.contrast?"1":"0"));
    });
    el("motionToggle").addEventListener("click", function () {
      state.reduceMotion=!state.reduceMotion;
      document.body.classList.toggle("reduce-motion",state.reduceMotion);
      this.setAttribute("aria-pressed",String(state.reduceMotion));
      setOptionLabel("motionToggle",state.reduceMotion?"모션 기본값":"모션 줄이기");
      resetSmoothScroll(el("workspace"));
      resetSmoothScroll(el("campaignList").parentElement);
      if(liveMode) sendHost("settings:motion:"+(state.reduceMotion?"1":"0"));
    });
    el("bootBrowse").addEventListener("click",function(){if(hostWorkDisplayed)showWorkBrowse(!workBrowsing);});
    el("workBrowseToggle").addEventListener("click",function(){
      workBrowseDefault=!workBrowseDefault;
      applyLivePreferences({browseWhileWorking:workBrowseDefault});
      if(liveMode)sendHost("settings:work-browse:"+(workBrowseDefault?"1":"0"));
    });
    el("skipBoot").addEventListener("click",finishBoot);
    el("replayBoot").addEventListener("click",function(){if(!liveMode)replayBoot();});
    el("minimizeApp").addEventListener("click",function () { sendHost("minimize"); });
    el("maximizeApp").addEventListener("click",function () { sendHost("maximize"); });
    el("closeApp").addEventListener("click",function () { sendHost("close"); });
    el("dragZone").addEventListener("mousedown",function(e) { if(e.button===0) sendHost("drag"); });
    el("dragZone").addEventListener("dblclick",function () { sendHost("maximize"); });
    document.addEventListener("keydown",function(e) {
      if(e.altKey&&e.key==="ArrowLeft"){e.preventDefault();navigateBack();return;}
      if ((e.key==="/" || e.key==="Divide") &&
          ["INPUT","TEXTAREA"].indexOf(document.activeElement.tagName)===-1) {
        e.preventDefault();setView("all");el("campaignSearch").focus();return;
      }
      if (e.key==="Escape") {hideCardMenu();finishBoot();if(document.activeElement)document.activeElement.blur();}
    if (e.ctrlKey && ["1","2","3","4"].indexOf(e.key)>=0) {
        e.preventDefault();setTheme(["terran","protoss","zerg","nova"][Number(e.key)-1],false);
      }
    });
  }
  function runSmoke() {
    var original = state.theme, results=[];
    function check(name, verdict) { results.push({name:name,ok:Boolean(verdict)}); }
    finishBoot();
    check("initial layout",Boolean(el("campaignList")&&el("hero")&&el("detailPanel")));
    check("real cards rendered",el("campaignList").querySelectorAll("[data-card]").length>=10);
    check("choose another campaign",selectCampaign("blackout")&&el("heroTitle").textContent==="블랙 아웃");
    setView("installed");
    check("installed filter",filtered().length>0&&filtered().every(function(c){return c.installed;}));
    setView("dedicated");
    check("dedicated filter",filtered().length>0&&filtered().every(function(c){return c.dedicated;}));
    setView("all");
    state.search="레볼라이트";renderCards();
    check("Korean search",filtered().length===1&&filtered()[0].id==="revolite");
    state.search="";renderCards();
    state.category="군심";renderCards();
    check("category filter",filtered().every(function(c){return c.category==="군심";}));
    state.category="all";renderCards();
    state.search="없는임무입니다";renderCards();
    check("empty search",filtered().length===0&&!el("emptyState").hidden);
    state.search="";renderCards();
    selectCampaign("blackout");
    var before=state.favorites.has("blackout");
    toggleFavorite();
    check("favorite toggles",state.favorites.has("blackout")!==before);
    setMode("list");check("list layout",el("campaignList").classList.contains("as-list"));
    setMode("grid");check("grid layout",!el("campaignList").classList.contains("as-list"));
    ["terran","protoss","zerg","nova"].forEach(function (theme) {
      setTheme(theme,false);
      check(theme+" visual",document.body.dataset.theme===theme&&el("brandOverline").textContent===THEMES[theme].overline);
      check(theme+" count badge",Number(el("resultCount").textContent)===filtered().length);
    });
    setTheme(original,false);selectCampaign("revolite");setView("all");
    el("launchButton").click();
    check("no real game launch",el("toastMessage").textContent.indexOf("실제 게임은 실행하지 않습니다")>=0);
    el("installButton").click();
    check("no real campaign install",el("toastMessage").textContent.indexOf("설치하거나 삭제하지 않습니다")>=0);
    replayBoot();
    check("boot replay",!el("boot").classList.contains("is-hidden") ||
      window.matchMedia("(prefers-reduced-motion: reduce)").matches);
    finishBoot();
    check("boot skip",el("boot").classList.contains("is-hidden"));
    return {passed:results.every(function(r){return r.ok;}),theme:original,checks:results,
      campaignCount:campaigns.length,documentTitle:document.title};
  }

  function setRealCampaigns(items,totalCount) {
    if (!liveMode || !Array.isArray(items)) return false;
    installedOnly=Number.isFinite(totalCount);
    registeredCount=installedOnly?totalCount:items.length;
    campaigns = items.filter(function(c) {
      return c && typeof c.id === "string" && typeof c.name === "string";
    }).map(function(c) {
      return {
        id:c.id,versionKey:c.versionKey||"", name:c.name, en:c.en||c.name, kind:c.kind||"캠페인",
        category:c.category||"자날", installed:!!c.installed, dedicated:!!c.dedicated,
        author:c.author||"제작자 미표기", img:c.img||"assets/hangar.jpg",
        desc:c.desc||"커캠 격납고에 등록된 캠페인입니다.",
        playSeconds:Number(c.playSeconds)||0,missions:Number(c.missions)||0,
        recommendations:Number.isFinite(Number(c.recommendations))&&c.recommendations!==null?Number(c.recommendations):null,
        updateState:c.updateState||"unknown",siteVersion:c.siteVersion||"",
        clears:Number(c.clears)||0,bankLinked:!!c.bankLinked,
        needsReview:!!c.needsReview,lastMission:c.lastMission||"",
        bankError:c.bankError||"",completedMissions:Array.isArray(c.completedMissions)?c.completedMissions:[]
      };
    });
    if (!campaigns.some(function(c){return c.id===state.selected;}))
      state.selected = campaigns.length ? campaigns[0].id : null;
    if (!liveDataReceived) {
      state.search="";state.category="all";state.view=liveMode?"archive":"all";
      el("campaignSearch").value="";el("campaignCategory").value="all";
    }
    liveDataReceived=true;
    refreshHeading();renderCards();refreshSelected();
    if (window.HangarFeatures) window.HangarFeatures.onView(state.view);
    setText("statusMessage", "실제 보관함 연결 완료 · 등록 " + registeredCount +
      "개 / 설치 " + campaigns.length + "개");
    return true;
  }
  function setFavoriteIds(ids) {
    if (!liveMode || !Array.isArray(ids)) return false;
    state.favorites = new Set(ids.filter(function(x){return typeof x==="string";}));
    renderCards();refreshSelected();
    return true;
  }

  window.runSmoke = runSmoke;
  window.UiLab = {selectCampaign:selectCampaign,setTheme:setTheme,setView:setView,filtered:filtered,
    replayBoot:replayBoot,finishBoot:finishBoot,mode:setMode,getState:function(){return state;},
    setRealCampaigns:setRealCampaigns,setFavoriteIds:setFavoriteIds,navigateBack:navigateBack,
    setActivationState:setActivationState,setWorkProgress:setWorkProgress,
    workWindowAttached:workWindowAttached,dismissWorkWindow:dismissWorkWindow,
    showWorkFullScreen:function(){showWorkBrowse(false);},
    setActivityTotals:setActivityTotals,setUpdateNotice:setUpdateNotice,
    revealCampaign:revealCampaign,
    getSelected:chosen, getCampaigns:function(){return campaigns.slice();},
    applyLivePreferences:applyLivePreferences,showNotificationCount:showNotificationCount,
    setThemeFromHost:function(theme){return setTheme(theme,false,true);},
    getWheelDistanceForTest:function(delta,mode){
      return wheelDistance({deltaY:delta,deltaMode:mode||0},el("campaignList").parentElement);
    }};
  document.addEventListener("DOMContentLoaded",function () {
    bindEvents();
    installActionIcons();
    if (window.HangarFeatures) window.HangarFeatures.init({
      sendHost:sendHost, setView:setView, selected:chosen, getState:function(){return state;},
      campaigns:function(){return campaigns.slice();},renderContacts:renderContactGrid,
      safeCover:safeCover,toast:toast, live:liveMode,icon:actionIcon
    });
    if (liveMode) {
      document.body.classList.add("embedded");
      document.title = "커캠 격납고 · 캠페인 아카이브";
      var bootCaption = document.querySelector(".boot-bottom > span");
      if (bootCaption) bootCaption.textContent = channelLabel+" · REAL CAMPAIGN DATA";
      var themeChooser = document.querySelector(".theme-switcher");
      if (themeChooser) themeChooser.setAttribute("aria-label", "화면 테마 선택");
      setText("statusMessage", "실제 캠페인 보관함 연결 중...");
      setText("systemStatus", "실제 보관함");
      var badge = document.querySelector(".build-flag");
      if (badge) badge.textContent = channelLabel;
      var installedCountLabel = el("statInstalled").parentElement.querySelector("small");
      if (installedCountLabel) installedCountLabel.textContent = "설치됨";
      var networkLabel = document.querySelector(".network-readout small");
      if (networkLabel) networkLabel.textContent = "격납고 연결";
      var safety = document.querySelector(".sidebar-foot>small");
      if (safety) safety.textContent = "실제 격납고 데이터 연결 · 설치 및 실행은 기존 안전 절차를 따릅니다.";
      var footer = document.querySelector(".status-end span:first-child");
      if (footer) footer.textContent = channelLabel+" / REAL CAMPAIGN DATA";
      var footerCaption = document.querySelector(".status-middle span:first-child");
      if (footerCaption) footerCaption.textContent = "CASE 20 · 실제 캠페인 아카이브";
      var heroHint = document.querySelector(".hero-meta span:first-child");
      if (heroHint) heroHint.textContent = "◈ 격납고 등록 정보";
      var settingsCopy=el("settingExplanation");
      if(settingsCopy) settingsCopy.textContent="이 테마의 시각 설정과 사용자 배치를 저장합니다. 캠페인과 경로 관리 도구도 아래에서 바로 사용할 수 있습니다.";
      var detailState = document.querySelector("#detailInstalled");
      if(detailState) detailState.previousElementSibling.textContent="설치 상태";
      var endline=document.querySelector(".hero-bottomline span:last-child");
      if(endline) endline.textContent="CONNECTED TO CAMPAIGN HANGAR";
      document.querySelectorAll(".action-button small").forEach(function(label) {label.remove();});
      el("replayBoot").hidden=true;
    }
    var startingTheme = getThemeFromQuery();
    // Distinct opening showcases make all three executable concepts visibly
    // different, without changing the campaign the user later selects.
    state.selected = liveMode ? null : startingTheme === "protoss" ? "dark" :
      startingTheme === "zerg" ? "hotslegend" :
      startingTheme === "nova" ? "nova" : "revolite";
    if (liveMode) state.view = "archive";
    setTheme(startingTheme,false);
    setMode("grid");
    renderCards();
    if (window.HangarFeatures) window.HangarFeatures.onView(state.view);
    updateClock();
    window.setInterval(updateClock,30000);
    if (liveMode || new URLSearchParams(window.location.search).has("smoke") ||
        new URLSearchParams(window.location.search).get("intro") === "0") finishBoot();
    else replayBoot();
    sendHost("ready:DOM_READY");
  });
})();
