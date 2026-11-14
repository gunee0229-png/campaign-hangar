(function () {
  "use strict";
  // User-owned layout presets are stored per theme; the existing archive
  // browser, campaign storage and installer remain owned by the native host.
  var THEME_IDS = ["terran", "protoss", "zerg", "nova"];
  var MENU_IDS = ["archive", "all", "wol", "hots", "lotv", "nova", "dedicated", "settings", "custom"];
  var CAMPAIGN_MENU_IDS = ["all","wol","hots","lotv","nova","dedicated"];
  var CONTACT_IDS = ["detail", "wol", "hots", "lotv", "nova", "dedicated"];
  var COLOR_GROUPS = [
    ["화면과 패널",["bg","panel","panelEnd"]],
    ["메뉴·목록·상세 배경",["menu","menuEnd","list","listEnd","detail","detailEnd"]],
    ["글자",["titleText","text","mutedText","buttonText"]],
    ["아이콘",["menuIcon","iconColor"]],
    ["강조와 상태",["accent","secondary","borderColor","selectedColor","progressColor"]]
  ];
  var COLOR_KEYS = [].concat.apply([],COLOR_GROUPS.map(function(g){return g[1];}));
  var COLOR_CAPTIONS = {accent:"주 강조색",secondary:"보조 강조색",bg:"전체 화면 배경",
    panel:"패널 시작색",panelEnd:"패널 끝색",menu:"메뉴 시작색",menuEnd:"메뉴 끝색",
    list:"목록 시작색",listEnd:"목록 끝색",detail:"상세 시작색",detailEnd:"상세 끝색",
    titleText:"제목 글자",text:"본문 글자",mutedText:"보조 글자",buttonText:"버튼 글자",
    menuIcon:"메뉴 아이콘",iconColor:"작업 아이콘",borderColor:"테두리",
    selectedColor:"선택 표시",progressColor:"작업 진행막대"};
  var THEME_COLORS = {
    terran:{accent:"#ffa950",secondary:"#ff6572",bg:"#080b12",panel:"#101823",text:"#f7f4ef"},
    protoss:{accent:"#e0b65a",secondary:"#58bdff",bg:"#050f21",panel:"#0a1e38",text:"#eef5ff"},
    zerg:{accent:"#d16aff",secondary:"#b6f260",bg:"#0b0814",panel:"#1a1026",text:"#f8ecff"},
    nova:{accent:"#39d8d0",secondary:"#8edbd6",bg:"#050c11",panel:"#0b1a22",text:"#e7f6f5"}
  };
  var COLOR_COMBINATIONS = [
    ["탈다림 · 흑철과 혈석","#110b0e","#28171e","#f45b69","#ce8b79","#ffeff1","#c4a3ac"],
    ["우모자 · 백철과 코발트","#131c28","#263546","#91b5d8","#d6e0e9","#edf2f6","#afbdcc"],
    ["레이너 특공대 · 청색과 백색","#0c1727","#1d3047","#75b5ff","#d4e5f5","#edf5ff","#a8bbd2"],
    ["이한 리 · 석재와 청록","#121e1d","#2c3c37","#82e4d0","#d1cec0","#f5f5eb","#b3c2b8"],
    ["멩스크 자치령 · 진홍과 황금","#1d0d12","#3a1c25","#f16b79","#e4bd70","#fff0e9","#cba8ab"],
    ["네라짐 · 공허와 녹색","#181025","#2d2040","#a9df91","#bf9df1","#f5efff","#b9aacb"],
    ["정화자 · 황금과 호박","#21180c","#443017","#f2c35b","#ffe3a0","#fff4d6","#cbbb94"],
    ["UED 원정 함대 · 백색과 청색","#101720","#283442","#e6edf5","#86a8d5","#f6f8fc","#b2c0cf"],
    ["켈모리안 조합 · 산업 녹색","#111e19","#243b2e","#8aca9b","#9ed8ce","#eef7ee","#a8c4b0"],
    ["미라 용병단 · 철갑과 분홍","#20131d","#3c273a","#f19ac9","#e4bf83","#fff0f9","#c9abc0"],
    ["레비아탄 무리 · 심해 자주","#160e25","#32213f","#bf8bdc","#cfa7c4","#f6edfb","#baa6c5"],
    ["원시 저그 · 대지와 정수","#191c12","#303924","#c5df83","#d7ad76","#f6f5e6","#bbc4a2"]
  ];
  var TEXT_FIELDS={sectionEyebrow:"상단 영문 머리말",heroKicker:"대표 영역 머리말",
    catalogEyebrow:"캠페인 목록 머리말",actionCaption:"빠른 작업 머리말",
    heroSideCaption:"대표 영역 장식 문구"};
  var TEXT_DEFAULTS={};
  var QUICK_SELECTORS={hero:"#hero",detail:"#detailPanel",art:"#detailArt",contactArt:"#contactArt"};
  var ELEMENT_LABELS={header:"상단 정보",hero:"대표 캠페인",actions:"작업 버튼 묶음",
    toolbar:"목록 도구",list:"캠페인 목록",detail:"상세정보",
    "contact-detail":"콘택트 상세","contact-wol":"자날 목록","contact-hots":"군심 목록",
    "contact-lotv":"공유 목록","contact-nova":"노바 목록","contact-dedicated":"전용 목록",
    launchButton:"캠페인 적용·실행",installButton:"설치·관리",detailButton:"상세 정보 보기",
    screenshotButton:"스크린샷 관리",reviewButton:"리뷰 달기",
    recommendButton:"추천",importZipButton:"ZIP 가져오기"};
  var PIECES = ["hero", "actions", "catalog"];
  var ACTION_IDS = ["launchButton", "installButton", "detailButton", "screenshotButton",
    "reviewButton", "recommendButton", "importZipButton"];
  var FREE_ACTION_IDS=ACTION_IDS.slice();
  var FREE_IDS = ["header", "hero", "actions", "toolbar", "list", "detail"];
  // Allow a scrolling free canvas: the original one-viewport ceiling snapped
  // lower list/detail panels hundreds of pixels upward when toggled on.
  var FREE_CANVAS_LENGTH=10000;
  var FREE_SELECTORS = {header:".workspace-header",hero:"#hero",actions:".action-strip",
    toolbar:".catalog-toolbar",list:".campaign-list-region",detail:"#detailPanel"};
  var FREE_MIN = {header:[300,80],hero:[260,155],actions:[245,65],
    toolbar:[290,95],list:[250,210],detail:[190,190]};
  // Coordinates are screen-independent per-mille values inside the workspace.
  // The five campaign controls remain real host-connected actions when moved.
  var FREE_DEFAULTS = {
    terran:{header:[20,10,960,115],hero:[20,140,630,250],actions:[20,408,630,85],
      toolbar:[20,507,630,160],list:[20,682,630,293],detail:[665,140,315,835]},
    protoss:{header:[20,10,960,110],hero:[20,140,960,235],actions:[20,390,960,80],
      toolbar:[20,485,960,140],list:[20,645,610,330],detail:[645,645,335,330]},
    zerg:{header:[20,10,960,115],hero:[360,140,620,250],actions:[360,408,620,85],
      toolbar:[360,507,620,160],list:[360,682,620,293],detail:[20,140,325,835]},
    nova:{header:[20,10,960,115],hero:[20,140,640,250],actions:[20,408,640,85],
      toolbar:[20,507,640,160],list:[20,682,640,293],detail:[675,140,305,835]}
  };
  var CONTACT_PANELS={detail:[20,140,470,390],wol:[510,140,470,390],hots:[20,550,470,390],
    lotv:[510,550,470,390],nova:[20,960,470,390],dedicated:[510,960,470,390]};
  CONTACT_IDS.forEach(function(slot){
    var id="contact-"+slot;FREE_IDS.push(id);FREE_MIN[id]=[120,120];
    FREE_SELECTORS[id]='[data-contact-cell="'+slot+'"]';
    QUICK_SELECTORS[id]=FREE_SELECTORS[id];
  });
  FREE_ACTION_IDS.forEach(function(id){FREE_IDS.push(id);FREE_MIN[id]=[90,40];FREE_SELECTORS[id]="#"+id;});
  function freeNode(id){
    return document.querySelector('[data-free-action="'+id+'"]')||document.querySelector(FREE_SELECTORS[id]);
  }
  function defaultFree(id){
    var result={};FREE_IDS.forEach(function(k){var a=FREE_DEFAULTS[id][k]||CONTACT_PANELS[k.slice(8)];
      if(FREE_ACTION_IDS.indexOf(k)>=0)a=[20+FREE_ACTION_IDS.indexOf(k)*138,408,132,65];
      result[k]={x:a[0],y:a[1],w:a[2],h:a[3]};});return result;
  }
  var DEFAULTS = {
    terran:{menu:"left",detail:"right",menuSize:246,heroHeight:215,detailWidth:270,
      listHeight:390,cols:2,spacing:13,cardHeight:88,parts:PIECES.slice(),nav:MENU_IDS.slice(),
      actions:ACTION_IDS.slice(),navGap:5,menuShape:"auto",free:false,presentation:"base",colors:{},contactOrder:CONTACT_IDS.slice(),panels:defaultFree("terran")},
    protoss:{menu:"top",detail:"bottom",menuSize:86,heroHeight:222,detailWidth:280,
      listHeight:410,cols:3,spacing:13,cardHeight:92,parts:PIECES.slice(),nav:MENU_IDS.slice(),
      actions:ACTION_IDS.slice(),navGap:4,menuShape:"auto",free:false,presentation:"base",colors:{},contactOrder:CONTACT_IDS.slice(),panels:defaultFree("protoss")},
    zerg:{menu:"left",detail:"left",menuSize:210,heroHeight:229,detailWidth:300,
      listHeight:390,cols:1,spacing:15,cardHeight:86,parts:PIECES.slice(),nav:MENU_IDS.slice(),
      actions:ACTION_IDS.slice(),navGap:5,menuShape:"auto",free:false,presentation:"base",colors:{},contactOrder:CONTACT_IDS.slice(),panels:defaultFree("zerg")},
    nova:{menu:"right",detail:"right",menuSize:223,heroHeight:224,detailWidth:295,
      listHeight:420,cols:2,spacing:10,cardHeight:85,parts:PIECES.slice(),nav:MENU_IDS.slice(),
      actions:ACTION_IDS.slice(),navGap:5,menuShape:"auto",free:false,presentation:"base",colors:{},contactOrder:CONTACT_IDS.slice(),panels:defaultFree("nova")}
  };
  var PRESETS = {
    base:{name:"기본형",note:"왼쪽 메뉴 · 위 이미지 · 목록/상세"},
    focus:{name:"집중형",note:"대표 이미지와 모든 상세정보 통합"},
    list:{name:"목록형",note:"상단 메뉴 · 이미지 없이 목록/상세"},
    contact:{name:"콘택트형",note:"상세·자날·군심·공유, 아래 노바·전용"}
  };
  var api, profiles={}, theme="terran", editing=false, draft=null, undo=[], redo=[], original=null,
    applyEveryTheme=false,palettesOpen=false,lastNativeColors="";
  var presetLibrary=[],presetName="내 커스텀 UI",presetPage=0;
  var dockFrame=0, lastDock="", archiveSuspended=false, activeRecordId="",
    workStart=0,workTimer=null,workDelay=null,workActive=false,
    dragging=null, resizing=null, moving=null, activeFreePanel="hero", temporaryTheme=false;
  var byId=function(id){return document.getElementById(id);};
  function clone(x){return JSON.parse(JSON.stringify(x));}
  function clamp(x,min,max,def) {x=Number(x);return Number.isFinite(x)?Math.max(min,Math.min(max,Math.round(x))):def;}
  function oneOf(x,arr,fallback){return arr.indexOf(x)>=0?x:fallback;}
  function validOrder(input,allowed) {
    var out=Array.isArray(input)?input.filter(function(x,i){return allowed.indexOf(x)>=0&&input.indexOf(x)===i;}):[];
    allowed.forEach(function(x){if(out.indexOf(x)<0)out.push(x);});
    return out;
  }
  function validActionOrder(input){
    var previous=Array.isArray(input)?input.map(function(id){
      return id==="favoriteButton"?"screenshotButton":id;
    }):[];
    var actions=validOrder(previous,ACTION_IDS);
    var missing=previous.indexOf("reviewButton")<0||previous.indexOf("recommendButton")<0;
    var tail=actions.length>=3&&actions[actions.length-3]==="importZipButton"&&
      actions[actions.length-2]==="reviewButton"&&actions[actions.length-1]==="recommendButton";
    if(missing||tail){
      actions=actions.filter(function(id){return id!=="reviewButton"&&id!=="recommendButton";});
      var beforeZip=actions.indexOf("importZipButton");
      actions.splice(beforeZip,0,"reviewButton","recommendButton");
    }
    return actions;
  }
  function validNav(input){
    var nav=validOrder(input,MENU_IDS),a=nav.indexOf("all"),w=nav.indexOf("wol");
    if(a>w){nav.splice(a,1);nav.splice(nav.indexOf("wol"),0,"all");}
    // The two campaign chapters must remain adjacent, including old saved layouts.
    var lotv=nav.indexOf("lotv"),nova=nav.indexOf("nova");
    if(lotv>=0&&nova!==lotv+1){nav.splice(nova,1);nav.splice(nav.indexOf("lotv")+1,0,"nova");}
    return nav;
  }
  function cleanColors(input){
    var result={},v=input&&typeof input==="object"?input:{};
    COLOR_KEYS.forEach(function(k){if(/^#[0-9a-fA-F]{6}$/.test(v[k]||""))result[k]=v[k].toLowerCase();});
    return result;
  }
  function cleanRect(input,fallback,key){
    var r=input&&typeof input==="object"?input:{},min=FREE_MIN[key];
    var w=clamp(r.w,1,1000,fallback.w),h=clamp(r.h,1,1000,fallback.h);
    if(r.locked===true)return {x:clamp(r.x,0,1000-w,fallback.x),
      y:clamp(r.y,0,FREE_CANVAS_LENGTH-h,fallback.y),w:w,h:h,locked:true};
    w=Math.max(10,Math.round(w/10)*10);h=Math.max(10,Math.round(h/10)*10);
    return {x:clamp(Math.round(clamp(r.x,0,1000-w,fallback.x)/10)*10,0,1000-w,0),
      y:clamp(Math.round(clamp(r.y,0,FREE_CANVAS_LENGTH-h,fallback.y)/10)*10,0,FREE_CANVAS_LENGTH-h,0),
      w:w,h:h,locked:false};
  }
  function sanitize(input,id) {
    var d=DEFAULTS[id],v=input||{};
    var menu=oneOf(v.menu,["top","left","right","bottom"],d.menu);
    var fallback=defaultFree(id),raw=v.panels&&typeof v.panels==="object"?v.panels:{},panels={};
    FREE_IDS.forEach(function(key){panels[key]=cleanRect(raw[key],fallback[key],key);});
    var quickSizes={};
    Object.keys(QUICK_SELECTORS).forEach(function(key){
      if(v.quickSizes&&v.quickSizes[key])quickSizes[key]={
        w:clamp(v.quickSizes[key].w,120,1200,300),h:clamp(v.quickSizes[key].h,80,1200,300)};
    });
    return {menu:menu,
      detail:oneOf(v.detail,["right","left","hidden"],d.detail==="bottom"?"right":d.detail),
      menuSize:clamp(v.menuSize,(menu==="top"||menu==="bottom")?70:170,340,d.menuSize),
      heroHeight:clamp(v.heroHeight,145,440,d.heroHeight),
      detailWidth:clamp(v.detailWidth,200,520,d.detailWidth),
      listHeight:clamp(v.listHeight,260,750,d.listHeight),
      cols:clamp(v.cols,1,4,d.cols),spacing:clamp(v.spacing,5,30,d.spacing),
      cardHeight:clamp(v.cardHeight,72,150,d.cardHeight),
      parts:validOrder(v.parts,PIECES),nav:validNav(v.nav),
      hiddenCampaigns:Array.isArray(v.hiddenCampaigns)?CAMPAIGN_MENU_IDS.filter(function(k){return v.hiddenCampaigns.indexOf(k)>=0;}):[],
      actions:validActionOrder(v.actions),navGap:clamp(v.navGap,0,18,d.navGap),
      menuShape:oneOf(v.menuShape,["auto","terran","protoss","zerg","nova"],"auto"),
      presentation:oneOf(v.presentation,Object.keys(PRESETS),"base"),
      colors:cleanColors(v.colors),contactOrder:validOrder(v.contactOrder,CONTACT_IDS),
      hiddenElements:Array.isArray(v.hiddenElements)?FREE_IDS.filter(function(k){return v.hiddenElements.indexOf(k)>=0;}):[],
      texts:Object.keys(TEXT_FIELDS).reduce(function(out,k){
        if(v.texts&&typeof v.texts[k]==="string")out[k]=v.texts[k].slice(0,120);return out;
      },{}),quickSizes:quickSizes,free:v.free===true,panels:panels};
  }
  function preset(id,key) {
    var v=clone(DEFAULTS[id]);
    v.presentation=key;
    v.menu="left";v.detail="right";v.menuSize=220;v.heroHeight=185;
    v.detailWidth=290;v.listHeight=440;v.cols=2;
    if(key==="focus"){v.detail="hidden";v.heroHeight=240;v.listHeight=345;v.cols=3;}
    if(key==="list"){v.menu="top";v.menuSize=86;v.heroHeight=145;v.detail="right";
      v.listHeight=660;v.detailWidth=390;v.cols=2;v.spacing=12;}
    if(key==="contact"){v.menu="left";v.menuSize=218;v.detail="hidden";v.spacing=12;v.cols=2;}
    return sanitize(v,id);
  }
  function emptyProfile(id){return {preset:"base",layout:preset(id,"base"),named:{}};}
  function ensureProfiles(){THEME_IDS.forEach(function(id){if(!profiles[id])profiles[id]=emptyProfile(id);});}
  function normalizeProfile(raw,id) {
    if(!raw||typeof raw!=="object"||!raw.layout)return emptyProfile(id);
    var v=raw||{},named={};
    if(v.named&&typeof v.named==="object")
      Object.keys(v.named).slice(0,8).forEach(function(k) {
        if(k.length>=1&&k.length<=30&&k!=="__proto__"&&k!=="constructor")
          named[k]=sanitize(v.named[k],id);
      });
    var saved=sanitize(v.layout,id);
    // Migrate the old label-only 'base' presets to the real basic layout.
    // Explicitly custom layouts and named saves remain untouched.
    if(v.preset==="base" && (saved.menu!=="left"||saved.detail!=="right")){
      var migrated=preset(id,"base");migrated.colors=saved.colors;
      saved=migrated;
    }
    return {preset:oneOf(v.preset,Object.keys(PRESETS).concat(["custom"]),"custom"),
      layout:saved,named:named};
  }
  function applyHostProfiles(data) {
    if(!data||typeof data!=="object")return false;
    THEME_IDS.forEach(function(id){profiles[id]=normalizeProfile(data[id],id);});
    if(!editing)apply();
    renderSettings();
    return true;
  }
  function current(){ensureProfiles();return editing?draft:profiles[theme].layout;}
  function captureFreeGeometry(v){
    // Start from the panels' *current* presentation rather than jumping
    // instantly to an unrelated default XY poster when free mode is checked.
    var frame=byId("workspace").getBoundingClientRect();
    var next=clone(v.panels);
    FREE_IDS.forEach(function(id){
      var node=freeNode(id);
      if(!node||getComputedStyle(node).display==="none")return;
      var rect=node.getBoundingClientRect(),old=next[id];
      if(rect.width<20||rect.height<20)return;
      var x=Math.max(0,Math.min(999,Math.round((rect.left-frame.left)*1000/frame.width)));
      var y=Math.max(0,Math.min(FREE_CANVAS_LENGTH-1,
        Math.round((rect.top-frame.top+byId("workspace").scrollTop)*1000/frame.height)));
      var w=Math.max(1,Math.round(rect.width*1000/frame.width));
      var h=Math.max(1,Math.round(rect.height*1000/frame.height));
      w=Math.min(1000,w);h=Math.min(1000,h);
      next[id]={x:Math.min(x,1000-w),y:Math.min(y,FREE_CANVAS_LENGTH-h),
        w:w,h:h,locked:!!old.locked};
    });
    return next;
  }
  function applyFreeGeometry(v){
    var workspace=byId("workspace"),view=api&&api.getState?api.getState().view:"all";
    var canFree=v.free && view!=="archive"&&view!=="settings"&&
      workspace.clientWidth>0&&workspace.clientHeight>0;
    FREE_ACTION_IDS.forEach(function(id){
      var button=byId(id),wrapper=document.querySelector('[data-free-action="'+id+'"]');
      if(canFree&&!wrapper){
        wrapper=document.createElement("div");wrapper.className="layout-free-action";
        wrapper.dataset.freeAction=id;workspace.appendChild(wrapper);wrapper.appendChild(button);
      }else if(!canFree&&wrapper){
        document.querySelector(".action-strip").appendChild(button);wrapper.remove();
      }
    });
    if(canFree)workspace.style.setProperty("--ui-free-canvas-scroll-height",
      Math.ceil(workspace.clientHeight*Math.max(1000,Math.max.apply(null,visibleFreeIds(v).map(function(id){
        return v.panels[id].y+v.panels[id].h;
      })))/1000)+"px");
    else workspace.style.removeProperty("--ui-free-canvas-scroll-height");
    var header=workspace.querySelector(".workspace-header"),activation=byId("activationReadout");
    if(header&&activation){
      if(canFree&&activation.parentNode!==header)header.appendChild(activation);
      else if(!canFree&&activation.parentNode===header)
        workspace.insertBefore(activation,header.nextSibling);
    }
    document.body.dataset.layoutFree=canFree?"true":"false";
    if(canFree){
      workspace.style.setProperty("--ui-free-grid-x",(workspace.clientWidth/100)+"px");
      workspace.style.setProperty("--ui-free-grid-y",(workspace.clientHeight/100)+"px");
    }
    FREE_IDS.forEach(function(id){
      var node=freeNode(id);if(!node)return;
      node.classList.toggle("layout-element-hidden",v.free&&(v.hiddenElements||[]).indexOf(id)>=0);
      if(canFree&&(id==="actions"||(v.hiddenElements||[]).indexOf(id)>=0))node.style.setProperty("display","none","important");
      else node.style.removeProperty("display");
      ["left","top","width","height"].forEach(function(prop){node.style.removeProperty(prop);});
      if(canFree){var rect=v.panels[id];
        node.style.setProperty("left",(rect.x/10)+"%","important");
        node.style.setProperty("top",(rect.y/10)+"%","important");
        node.style.setProperty("width",(rect.w/10)+"%","important");
        node.style.setProperty("height",(rect.h/10)+"%","important");}
    });
    if(editing)document.body.dataset.layoutFreeFallback=v.free&&!canFree?"true":"false";
    else document.body.dataset.layoutFreeFallback="false";
  }
  function themeColors(v){
    var css=document.body.style,base=THEME_COLORS[theme];
    COLOR_KEYS.forEach(function(k){
      var value=(v.colors&&v.colors[k])||base[k];
      if(v.colors&&v.colors[k])css.setProperty("--"+k,value);
      else css.removeProperty("--"+k);
      if(k==="accent"||k==="secondary"){
        var rgb=[1,3,5].map(function(i){return parseInt(value.slice(i,i+2),16);}).join(",");
        if(v.colors&&v.colors[k])css.setProperty("--"+k+"-rgb",rgb);
        else css.removeProperty("--"+k+"-rgb");
      }
    });
    ["accent-bright","edge","edge-faint","surface"].forEach(function(k){css.removeProperty("--"+k);});
    if(v.colors&&v.colors.accent){
      css.setProperty("--edge","rgba(var(--accent-rgb),.47)");
      css.setProperty("--edge-faint","rgba(var(--accent-rgb),.18)");
      css.setProperty("--accent-bright",v.colors.accent);
    }
    if(v.colors&&v.colors.panel)css.setProperty("--surface",v.colors.panel);
    if(api&&api.live){
      var native=JSON.stringify({theme:theme,colors:v.colors||{}});
      if(native!==lastNativeColors){lastNativeColors=native;api.sendHost("native-colors:"+native);}
    }
  }
  function apply() {
    var v=current(),body=document.body,css=body.style;
    body.dataset.layoutActive="true";body.dataset.layoutMenu=v.menu;body.dataset.layoutDetail=v.detail;
    body.dataset.menuShape=v.menuShape==="auto"?theme:v.menuShape;
    body.dataset.layoutPreset=v.presentation;
    Object.keys(TEXT_FIELDS).forEach(function(k){
      var node=byId(k);if(!node)return;
      if(!Object.prototype.hasOwnProperty.call(TEXT_DEFAULTS,k))TEXT_DEFAULTS[k]=node.textContent;
      node.textContent=Object.prototype.hasOwnProperty.call(v.texts,k)?v.texts[k]:TEXT_DEFAULTS[k];
    });
    themeColors(v);
    body.dataset.uiSmallHero=v.heroHeight<211?"true":"false";
    css.setProperty("--ui-menu-width",v.menuSize+"px");
    css.setProperty("--ui-menu-height",Math.max(70,Math.min(104,v.menuSize))+"px");
    css.setProperty("--ui-hero-height",v.heroHeight+"px");
    css.setProperty("--ui-detail-width",v.detailWidth+"px");
    css.setProperty("--ui-list-height",v.listHeight+"px");
    css.setProperty("--ui-card-columns",v.cols);
    css.setProperty("--ui-contact-columns",Math.min(3,v.cols));
    body.dataset.contactColumns=String(Math.min(3,v.cols));
    css.setProperty("--ui-spacing",v.spacing+"px");
    css.setProperty("--ui-card-height",v.cardHeight+"px");
    css.setProperty("--ui-nav-gap",v.navGap+"px");
    var selectors={hero:"#hero",actions:".action-strip",catalog:"#catalogPanel"};
    v.parts.forEach(function(id,index) {var e=document.querySelector(selectors[id]);if(e)e.style.order=index+1;});
    document.querySelector(".workspace-header").style.order=0;
    document.querySelectorAll(".side-nav [data-view]").forEach(function(node){
      // The layout editor is a protected action, not a reorderable or
      // hideable campaign chapter. Old saved menu orders stay intact.
      node.style.order=node.dataset.view==="custom"?99:v.nav.indexOf(node.dataset.view);
      node.dataset.campaignHidden=String((v.hiddenCampaigns||[]).indexOf(node.dataset.view)>=0);
      node.hidden=v.presentation==="contact"&&node.dataset.view!=="all";
      var toggle=node.querySelector(".nav-visibility-toggle");
      if(toggle){toggle.textContent=node.dataset.campaignHidden==="true"?"＋":"×";
        toggle.title=node.dataset.campaignHidden==="true"?"캠페인 메뉴 다시 표시":"캠페인 메뉴 숨기기";
        toggle.setAttribute("aria-label",toggle.title);}
    });
    ACTION_IDS.forEach(function(id){var node=byId(id);if(node)node.style.order=v.actions.indexOf(id);});
    var rail=document.querySelector(".action-rail-label");if(rail)rail.style.order=99;
    var contact=byId("contactGrid");
    if(contact){
      contact.style.order=4;
      contact.hidden=v.presentation!=="contact"||["archive","settings"].indexOf(api.getState().view)>=0;
      v.contactOrder.forEach(function(k,i){var cell=contact.querySelector('[data-contact-cell="'+k+'"]');
        if(cell){cell.style.order=i;cell.draggable=editing&&v.presentation==="contact"&&!v.free;
          cell.classList.toggle("layout-drag-item",cell.draggable);}});
      if(!contact.hidden&&api.renderContacts)api.renderContacts();
    }
    applyFreeGeometry(v);
    applyQuickSizes(v);
    ensureQuickResize();
    renderSettings();
    document.querySelectorAll('.sidebar [data-view="custom"]').forEach(function(node){
      node.classList.toggle("is-active",editing);node.setAttribute("aria-current",editing?"page":"false");
    });
    updateNavigationPresentation();
    scheduleDock();
  }
  function updateNavigationPresentation(){
    var menu=current().menu,vertical=menu==="left"||menu==="right";
    document.body.dataset.navCompact=(!vertical||current().menuSize<194)?"true":"false";
    var sidebar=document.querySelector(".sidebar"),radar=document.querySelector(".sidebar-graphic");
    if(!sidebar||!radar)return;
    // Measure while the decoration is hidden; measuring scrollHeight with
    // radar in-flow otherwise causes a true/false oscillation on resize.
    document.body.dataset.radarSpace="false";
    var free=sidebar.clientHeight-sidebar.scrollHeight;
    // Sidebars on compact windows must prioritize navigation and quick links.
    var visible=vertical&&sidebar.clientHeight>=720&&free>=132;
    document.body.dataset.radarSpace=visible?"true":"false";
  }
  function applyQuickSizes(v){
    var contactGrid=byId("contactGrid");
    if(contactGrid)contactGrid.style.removeProperty("grid-template-rows");
    Object.keys(QUICK_SELECTORS).forEach(function(id){
      var node=document.querySelector(QUICK_SELECTORS[id]);if(!node)return;
      if(node.dataset.quickSized==="true"){
        (v.free?["min-height","max-height","flex"]:["width","height","min-height","max-height","flex"]).forEach(function(k){node.style.removeProperty(k);});
        node.dataset.quickSized="false";
      }
      var size=v.quickSizes[id];if(v.free||!size)return;
      node.dataset.quickSized="true";
      node.style.setProperty("height",size.h+"px","important");
      node.style.setProperty("min-height",Math.min(size.h,120)+"px","important");
      node.style.setProperty("max-height",size.h+"px","important");
      node.style.setProperty("flex","none","important");
      if(id==="art"&&v.presentation==="base"){
        node.style.setProperty("min-height",size.h+"px","important");
        node.style.setProperty("max-height","none","important");
        node.style.setProperty("flex","1 0 "+size.h+"px","important");
      }
      if(id.indexOf("contact-")===0)node.style.setProperty("width","min(100%,"+size.w+"px)","important");
    });
    if(contactGrid&&!v.free&&v.presentation==="contact"){
      var cells=v.contactOrder,columns=getComputedStyle(contactGrid).gridTemplateColumns.split(" ").length;
      var rows=[],rect=byId("workspace").getBoundingClientRect();
      var fallback=Math.max(336,(rect.height-180)/2);
      for(var i=0;i<cells.length;i+=columns){
        var heights=cells.slice(i,i+columns).map(function(k){
          var size=v.quickSizes["contact-"+k];return size?size.h:fallback;});
        rows.push(Math.max.apply(null,heights)+"px");
      }
      contactGrid.style.setProperty("grid-template-rows",rows.join(" "),"important");
    }
  }
  function ensureQuickResize(){
    var targets=[["menu",".sidebar"]];
    Object.keys(QUICK_SELECTORS).forEach(function(id){targets.push([id,QUICK_SELECTORS[id]]);});
    targets.forEach(function(pair){
      var id=pair[0],node=document.querySelector(pair[1]);if(!node||
        Array.from(node.children).some(function(n){return n.hasAttribute("data-quick-resize");}))return;
      ["e","s","se"].forEach(function(edge){
        var grip=document.createElement("span");grip.className="quick-resize quick-resize-"+edge;
        grip.dataset.quickResize=id;grip.dataset.quickEdge=edge;grip.setAttribute("role","separator");
        grip.setAttribute("aria-label",id+" 테두리 크기 조절");grip.title="테두리를 끌어 크기 조절";
        node.appendChild(grip);
        var drag=null;
        grip.addEventListener("pointerdown",function(e){
          if(e.button!==0||editing||current().free)return;
          e.preventDefault();e.stopPropagation();
          var r=node.getBoundingClientRect();drag={x:e.clientX,y:e.clientY,w:r.width,h:r.height,before:clone(profiles[theme])};
          if(e.isTrusted)grip.setPointerCapture(e.pointerId);
        });
        grip.addEventListener("pointermove",function(e){
          if(!drag)return;
          var v=clone(drag.before.layout),dx=e.clientX-drag.x,dy=e.clientY-drag.y;
          if(id==="menu"){
            var horizontal=v.menu==="top"||v.menu==="bottom";
            v.menuSize=horizontal?drag.h+dy*(v.menu==="bottom"?-1:1):drag.before.layout.menuSize+dx*(v.menu==="right"?-1:1);
          }else if(id==="hero"){
            v.heroHeight=drag.h+dy;
            v.quickSizes.hero={w:Math.round(drag.w),h:clamp(drag.h+dy,145,440,drag.h)};
          }
          else {
            v.quickSizes[id]={w:Math.round(drag.w+(edge==="s"?0:dx)),h:Math.round(drag.h+(edge==="e"?0:dy))};
            if(id==="detail"&&edge!=="s")v.detailWidth=drag.before.layout.detailWidth+dx*(v.detail==="right"?-1:1);
          }
          profiles[theme].layout=sanitize(v,theme);profiles[theme].preset="custom";apply();
        });
        function finish(e){
          if(!drag)return;
          if(e.type==="pointercancel"){profiles[theme]=drag.before;drag=null;apply();return;}
          var changed=JSON.stringify(profiles[theme])!==JSON.stringify(drag.before);drag=null;
          if(changed)persistProfiles();
        }
        grip.addEventListener("pointerup",finish);grip.addEventListener("pointercancel",finish);
      });
    });
  }
  function renderSettings() {
    if(!byId("layoutSettings"))return;
    ensureProfiles();
    var p=profiles[theme],html='<span class="setting-overline">배치</span><strong>테마별 레이아웃</strong>'+
      '<p class="layout-settings-intro">배치를 선택하면 실제 화면 구성이 바뀝니다. 세부 배치와 색상은 ‘커스텀 편집’ 메뉴에서 조정합니다.</p>'+
      '<div class="layout-presets">';
    Object.keys(PRESETS).forEach(function(key){
      html+='<button class="layout-preset" type="button" data-preset="'+key+'" aria-pressed="'+(p.preset===key)+'">'+
        '<span class="preset-map preset-map-'+key+'" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i><i></i></span>'+
        '<strong>'+PRESETS[key].name+'</strong></button>';
    });
    html+='</div><div class="layout-settings-actions">'+
      '<small>현재 테마: '+({terran:"테란",protoss:"프로토스",zerg:"저그",nova:"노바"})[theme]+
      ' · '+(p.preset==="custom"||!PRESETS[p.preset]?"사용자 지정":PRESETS[p.preset].name)+'</small></div>';
    byId("layoutSettings").innerHTML=html;
    byId("layoutSettings").querySelectorAll("[data-preset]").forEach(function(b){
      b.addEventListener("click",function(){applyPreset(b.dataset.preset);});
    });
  }
  function persistProfiles(){
    if(api.live)api.sendHost("layout-save:"+JSON.stringify(profiles));
    else try{localStorage.setItem("campaign-hangar-theme-layouts",JSON.stringify(profiles));}catch(_){}
  }
  function applyPreset(key){
    if(!PRESETS[key])return;
    if(editing)cancelEdit();
    var colors=clone(profiles[theme].layout.colors||{});
    profiles[theme].layout=preset(theme,key);
    profiles[theme].layout.colors=colors;
    profiles[theme].preset=key;
    apply();persistProfiles();
    // A click from Settings must visibly switch the actual page immediately,
    // not leave the user staring at an unchanged settings card.
    if(api.getState().view==="settings")api.setView("all");
    api.toast(PRESETS[key].name+" 배치를 적용했습니다.");
  }
  function record(){if(!editing)return;undo.push(clone(draft));if(undo.length>32)undo.shift();redo=[];}
  function change(field,value) {
    if(!editing)return;
    var next=clone(draft);
    if(field==="presentation"){
      next=preset(theme,value);
      next.colors=clone(draft.colors||{});
      next.contactOrder=clone(draft.contactOrder||CONTACT_IDS);
      next.nav=validNav(draft.nav);
      next.hiddenCampaigns=clone(draft.hiddenCampaigns||[]);
    }else if(field==="free"&&value===true&&!draft.free){
      next.panels=captureFreeGeometry(draft);
      next.free=true;
    }else next[field]=value;
    next=sanitize(next,theme);
    if(JSON.stringify(next)===JSON.stringify(draft))return;
    record();draft=next;apply();syncEditorHandles();renderEditor();
  }
  function beginEdit(presetKey){
    if(editing)cancelEdit();
    editing=true;temporaryTheme=true;original=clone(profiles[theme]);applyEveryTheme=false;
    draft=presetKey?preset(theme,presetKey):clone(profiles[theme].layout);
    undo=[];redo=[];
    document.body.dataset.layoutEditing="true";
    if(api.getState().view==="settings"||api.getState().view==="archive")
      api.setView("all");
    byId("layoutEditor").hidden=api.live;
    syncEditorHandles();apply();renderEditor();
    if(api.live){api.sendHost("preset-request");api.sendHost("editor-open");}
  }
  function finishEdit(commit) {
    if(!editing)return;
    if(commit){
      profiles[theme].layout=sanitize(draft,theme);
      profiles[theme].preset="custom";
      if(applyEveryTheme)THEME_IDS.forEach(function(id){
        if(id===theme)return;
        profiles[id].layout=sanitize(draft,id);profiles[id].preset="custom";
      });
      persistProfiles();
    }else profiles[theme]=original;
    editing=false;draft=null;original=null;temporaryTheme=false;applyEveryTheme=false;
    document.body.dataset.layoutEditing="false";byId("layoutEditor").hidden=true;
    setDraggables(false);removeResizeGrips();removeFreeHandles();apply();
    if(api.live)api.sendHost("editor-close");
    if(commit)api.toast(api.live?"레이아웃 저장을 요청했습니다.":"이 테마의 사용자 레이아웃을 저장했습니다.");
  }
  function cancelEdit(){finishEdit(false);}
  function handleBack(){
    if(!byId("recordOverlay").hidden){closeRecord();return true;}
    if(!byId("manageOverlay").hidden){
      if(!byId("manageActionPanel").hidden){byId("manageActionBack").click();return true;}
      closeManage();return true;
    }
    if(editing){
      if(draft&&draft.free){finishEdit(false);return true;}
      if(byId("layoutExitConfirm")&&!byId("layoutExitConfirm").hidden)byId("layoutExitConfirm").hidden=true;
      else requestCloseEdit();
      return true;
    }
    return false;
  }
  function requestCloseEdit(){
    if(!editing)return;
    if(JSON.stringify(draft)===JSON.stringify(original.layout) && !applyEveryTheme){
      finishEdit(false);return;
    }
    var confirm=byId("layoutExitConfirm");
    if(confirm){confirm.hidden=false;confirm.querySelector('[data-close-choice="yes"]').focus();syncExternalEditor();}
  }
  function closeEditorWindow(){
    if(!editing)return;
    var confirm=byId("layoutExitConfirm");
    if(confirm&&!confirm.hidden){finishEdit(false);return;}
    requestCloseEdit();
  }
  function button(text,cmd,cls){return '<button type="button" data-edit-action="'+cmd+'"'+(cls?' class="'+cls+'"':'')+'>'+text+'</button>';}
  function shareSnapshot(){
    if(!editing||!draft)return null;
    var name=presetName.trim();
    if(!name||name.length>48){api.toast("디자인 이름을 1~48자로 입력해 주세요.");return null;}
    return {format:"campaign-hangar-ui",version:1,name:name,theme:theme,
      layout:sanitize(draft,theme)};
  }
  function setPresetLibrary(items){
    if(!Array.isArray(items))return false;
    presetLibrary=items.filter(function(p){return p&&typeof p.id==="string"&&
      /^[a-f\d]{32}$/i.test(p.id)&&typeof p.name==="string"&&p.layout;}).slice(0,64);
    presetPage=Math.min(presetPage,Math.max(0,Math.ceil(presetLibrary.length/12)-1));
    if(editing)renderEditor();
    return true;
  }
  function persistDemoPresetLibrary(){
    if(!api.live)try{localStorage.setItem("campaign-hangar-custom-presets",JSON.stringify(presetLibrary));}catch(_){}
  }
  function saveDraftToLibrary(){
    var data=shareSnapshot();if(!data)return;
    if(api.live){api.sendHost("preset-save:"+JSON.stringify(data));return;}
    if(presetLibrary.length>=64){api.toast("보관함에는 최대 64개까지 저장할 수 있습니다.");return;}
    presetLibrary.unshift({id:"00000000000000000000000000000000".replace(/0/g,function(){return Math.floor(Math.random()*16).toString(16);}),
      name:data.name,theme:data.theme,layout:data.layout,shared:false,created:new Date().toISOString()});
    presetPage=0;
    persistDemoPresetLibrary();renderEditor();api.toast("디자인을 보관함에 저장했습니다.");
  }
  function applyStoredPreset(id){
    var item=presetLibrary.find(function(p){return p.id===id;});
    if(!item)return false;
    var next=sanitize(item.layout,theme);
    profiles[theme].layout=next;profiles[theme].preset="custom";
    // The user explicitly applied a saved design; later Cancel must not undo it.
    if(editing){original=clone(profiles[theme]);draft=clone(next);undo=[];redo=[];}
    apply();persistProfiles();
    if(editing){syncEditorHandles();renderEditor();}
    api.toast("‘"+item.name+"’ 디자인을 현재 화면에 적용했습니다.");
    return true;
  }
  function applyImportedPreset(item){
    if(!item||!item.layout||typeof item.id!=="string")return false;
    presetLibrary=[item].concat(presetLibrary.filter(function(p){return p.id!==item.id;})).slice(0,64);
    presetPage=0;
    return applyStoredPreset(item.id);
  }
  function presetFeedback(message){api.toast(String(message||"커스텀 UI 작업을 마쳤습니다."));}
  function renderPresetLibrary(){
    return '<section class="layout-library" aria-label="내 커스텀 UI 보관함">'+
      '<div class="layout-library-heading"><strong>내 디자인 보관함</strong><small>저장된 '+presetLibrary.length+'개 디자인 · 공유받은 UI 포함</small></div>'+
      '<label class="layout-library-name">디자인 이름<input type="text" maxlength="48" data-preset-name="1" value="'+escapeHtml(presetName)+'" placeholder="디자인 이름"></label>'+
      '<div class="layout-library-actions">'+
      button("보관함에 저장","library-save","layout-primary")+
      button("내보내기","library-export")+
      button("불러오기","library-import")+'</div>'+
      '<div class="layout-library-list">'+
      (presetLibrary.length?presetLibrary.slice(presetPage*12,presetPage*12+12).map(function(p){
        return '<div class="layout-library-item"><div><strong title="'+escapeHtml(p.name)+'">'+escapeHtml(p.name)+
          '</strong><small>'+(p.shared?'공유받은 UI':'내가 저장한 UI')+' · '+({terran:"테란",protoss:"프로토스",zerg:"저그",nova:"노바"}[p.theme]||"테마")+'</small></div>'+
          '<div class="layout-library-item-actions">'+button("적용","library-apply-"+p.id)+
          button("파일로","library-export-"+p.id)+button("삭제","library-delete-"+p.id)+'</div></div>';
      }).join(""):'<p class="layout-library-empty">저장된 디자인이 없습니다. 편집한 UI를 보관하거나 공유 파일을 불러오세요.</p>')+
      '</div>'+
      (presetLibrary.length>12?'<div class="layout-library-pages">'+
        button("이전","library-prev")+
        '<span>'+(presetPage+1)+' / '+Math.ceil(presetLibrary.length/12)+'</span>'+
        button("다음","library-next")+'</div>':'')+'</section>';
  }
  function select(field,title,options,value) {
    return '<label>'+title+'<select data-layout-field="'+field+'">'+options.map(function(o){
      return '<option value="'+o[0]+'"'+(o[0]===value?' selected':'')+'>'+o[1]+'</option>';
    }).join("")+'</select></label>';
  }
  function range(field,title,min,max,value,unit) {
    return '<label class="layout-range">'+title+' <b>'+value+(unit||"")+'</b>'+
      '<input type="range" min="'+min+'" max="'+max+'" step="1" value="'+value+
      '" data-layout-range="'+field+'"></label>';
  }
  function freeNumber(field,value){
    return '<label>'+({x:'가로 위치',y:'세로 위치',w:'너비',h:'높이'})[field]+
      ' (%)<input type="number" data-free-coordinate="'+field+'" min="0" max="100" step="1" value="'+
      Math.round(value/10)+'"></label>';
  }
  function renderEditor(){
    if(!editing)return;
    var v=draft;
    var html='<div class="layout-editor-header"><div><strong>커스텀 편집</strong>'+
      '<small>화면 배치와 색상을 편집합니다.</small></div>'+
      button("저장","apply","layout-primary")+'</div>'+
      '<div class="layout-editor-actions">'+button("되돌리기","undo")+button("다시 실행","redo")+
      button("취소","cancel")+'</div>'+
      '<div class="layout-editor-dashboard">'+
      '<section class="layout-editor-section layout-editor-section--geometry"><h3>화면 배치</h3>'+
      '<div class="layout-editor-choices" aria-label="기본 배치 옵션">'+
      select("presentation","배치 유형",Object.keys(PRESETS).map(function(k){return [k,PRESETS[k].name];}),v.presentation)+
      select("menuShape","메뉴 모양",[["auto","현재 종족"],["terran","테란 장갑형"],["protoss","프로토스 수정형"],["zerg","저그 생체형"],["nova","노바 전술형"]],v.menuShape)+
      select("menu","메뉴바 위치",[["left","좌측"],["top","상단"],["right","우측"],["bottom","하단"]],v.menu)+
      (["base","list"].indexOf(v.presentation)>=0?select("detail","상세정보 배치",[["right","우측"],["left","좌측"],["hidden","숨김"]],v.detail):'')+'</div>'+
      '<h3 class="layout-editor-subheading">크기 및 간격</h3>'+
      '<div class="layout-editor-metrics" aria-label="크기 및 간격 옵션">'+
      range("menuSize","메뉴 크기",v.menu==="left"||v.menu==="right"?170:70,v.menu==="left"||v.menu==="right"?340:104,v.menuSize,"px")+
      (["base","focus"].indexOf(v.presentation)>=0?range("heroHeight","대표 이미지 높이",145,440,v.heroHeight,"px"):'')+
      (["base","list"].indexOf(v.presentation)>=0&&v.detail!=="hidden"?range("detailWidth","상세 패널 너비",200,520,v.detailWidth,"px"):'')+
      (["base","focus"].indexOf(v.presentation)>=0?range("listHeight","목록 영역 높이",260,750,v.listHeight,"px"):'')+
      range("cols","목록 한 줄 카드",1,v.presentation==="contact"?3:4,Math.min(v.cols,v.presentation==="contact"?3:4),"개")+
      range("spacing","패널 간격",5,30,v.spacing,"px")+
      (v.presentation!=="contact"?range("cardHeight","카드 최소 높이",72,150,v.cardHeight,"px"):'')+
      (v.presentation!=="contact"?range("navGap","메뉴 버튼 간격",0,18,v.navGap,"px"):'')+'</div></section>'+
      '<section class="layout-editor-section layout-editor-section--colors">'+
      '<section class="layout-color-panel"><strong>테마 색상 편집</strong><small>현재 테마에만 적용됩니다. 저장 전에는 미리보기입니다.</small>'+
      button("추천 색상 조합","palettes")+
      '<div class="layout-palettes"'+(palettesOpen?'':' hidden')+'>'+
      COLOR_COMBINATIONS.map(function(p,i){return '<button type="button" data-edit-action="palette-'+i+'">'+
        '<span class="palette-swatches" aria-hidden="true">'+p.slice(1,5).map(function(c){return '<i style="background:'+c+'"></i>';}).join("")+
        '</span>'+p[0]+'</button>';}).join("")+'</div>'+
      '<div class="layout-color-groups">'+
      COLOR_GROUPS.map(function(g,i){return '<fieldset class="layout-color-group layout-color-group--'+
        ["panel","surfaces","text","icons","status"][i]+'"><legend>'+g[0]+'</legend><div class="layout-color-grid">'+g[1].map(function(k){return '<label>'+COLOR_CAPTIONS[k]+
        '<input type="color" data-layout-color="'+k+'" value="'+editorColor(k)+'">'+
        button("기본값","color-reset-"+k)+'</label>';}).join("")+'</div></fieldset>';}).join("")+'</div>'+
      '<small>시작색·끝색을 다르게 지정하면 그라데이션, 같게 지정하면 단색입니다. 메뉴·목록·상세는 각각 조정할 수 있습니다.</small></section></section>'+
      '<section class="layout-editor-section layout-editor-section--extras"><h3>추가 편집</h3>'+
      (v.presentation==="contact"?'<p class="layout-editor-help">콘택트형은 화면의 여섯 구역을 드래그해 순서를 바꿀 수 있습니다. 아래로 스크롤하면 노바·전용이 이어집니다.</p>':'')+
      '<div class="layout-quick-actions">'+button(v.free?"자유 배치 해제":"자유 배치","free")+
      button("기본색 되돌리기","reset-colors")+'</div>'+
      (v.free?'<section class="layout-free-options"><h3>꾸밈 문구</h3>'+
        Object.keys(TEXT_FIELDS).map(function(k){return '<label>'+TEXT_FIELDS[k]+
          '<input type="text" maxlength="120" data-layout-text="'+k+'" value="'+
          escapeHtml(Object.prototype.hasOwnProperty.call(v.texts,k)?v.texts[k]:TEXT_DEFAULTS[k]||"")+'"></label>';}).join("")+
        '<h3>숨긴 요소 · 다시 넣기</h3><div class="layout-hidden-list">'+
        (v.hiddenElements.length?v.hiddenElements.map(function(k){return button(ELEMENT_LABELS[k]+" 복원","restore-"+k);}).join(""):
        '<p>숨긴 요소가 없습니다. 패널이나 버튼의 ×를 누르면 여기에 표시됩니다.</p>')+
        '</div></section>':'')+
      '</section><section class="layout-editor-section layout-editor-section--saved">'+
      renderPresetLibrary()+'</section></div>'+
      '<div class="layout-editor-footer">'+
      '<div class="layout-editor-actions">'+button("기본값 되돌리기","reset")+
      '<label class="layout-apply-all"><input type="checkbox" id="layoutAllThemes"'+
      (applyEveryTheme?' checked':'')+'> 모든 테마 적용</label></div></div>'+
      '<div class="layout-exit-confirm" id="layoutExitConfirm" hidden role="dialog" aria-modal="true" aria-label="레이아웃 저장 확인">'+
      '<strong>저장하시겠습니까?</strong><small>예: 저장 후 닫기 / 아니오: 변경 내용 버리고 닫기</small>'+
      '<div class="layout-exit-buttons"><button type="button" data-close-choice="yes">예</button>'+
      '<button type="button" data-close-choice="no">아니오</button></div></div>';
    byId("layoutEditor").innerHTML=html;
    byId("layoutEditor").querySelectorAll("[data-layout-text]").forEach(function(e){
      e.addEventListener("change",function(){
        record();draft.texts[e.dataset.layoutText]=e.value.slice(0,120);apply();syncExternalEditor();
      });
    });
    byId("layoutEditor").querySelectorAll("[data-preset-name]").forEach(function(e){
      e.addEventListener("input",function(){presetName=e.value.slice(0,48);});
    });
    byId("layoutEditor").querySelectorAll("[data-layout-field]").forEach(function(e){
      e.addEventListener("change",function(){change(e.dataset.layoutField,e.value);});
    });
    byId("layoutEditor").querySelectorAll("[data-layout-color]").forEach(function(e){
      e.addEventListener("input",function(){
        if(!e.dataset.beforeColor)e.dataset.beforeColor=JSON.stringify(draft);
        draft.colors[e.dataset.layoutColor]=e.value;themeColors(draft);
      });
      e.addEventListener("change",function(){
        if(e.dataset.beforeColor){undo.push(JSON.parse(e.dataset.beforeColor));redo=[];delete e.dataset.beforeColor;}
        apply();
      });
    });
    byId("layoutAllThemes").addEventListener("change",function(){applyEveryTheme=this.checked;});
    byId("layoutEditor").querySelectorAll("[data-close-choice]").forEach(function(e){
      e.addEventListener("click",function(){finishEdit(e.dataset.closeChoice==="yes");});
    });
    byId("layoutEditor").querySelectorAll("[data-layout-range]").forEach(function(e){
      e.addEventListener("change",function(){
        var before=e.dataset.rangeStart?JSON.parse(e.dataset.rangeStart):clone(draft);
        delete e.dataset.rangeStart;
        if(JSON.stringify(before)!==JSON.stringify(draft)){undo.push(before);redo=[];}
        apply();renderEditor();
      });
      e.addEventListener("input",function(){
        if(!e.dataset.rangeStart)e.dataset.rangeStart=JSON.stringify(draft);
        e.previousElementSibling.textContent=e.value+(e.dataset.layoutRange==="cols"?"개":"px");
        var p=clone(draft);p[e.dataset.layoutRange]=Number(e.value);
        draft=sanitize(p,theme);apply();
      });
    });
    byId("layoutEditor").querySelectorAll("[data-edit-action]").forEach(function(e){
      e.addEventListener("click",function(){editorAction(e.dataset.editAction);});
    });
    syncExternalEditor();
  }
  function editorColor(key){
    var value=draft.colors[key]||THEME_COLORS[theme][key]||
      getComputedStyle(document.body).getPropertyValue("--"+key).trim();
    if(/^#[0-9a-f]{6}$/i.test(value))return value;
    var rgb=value.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/);
    return rgb?"#"+rgb.slice(1,4).map(function(n){return Number(n).toString(16).padStart(2,"0");}).join(""):THEME_COLORS[theme].panel;
  }
  function syncExternalEditor(){
    if(!api||!api.live||!editing)return;
    var style=getComputedStyle(document.body),colors={};
    ["bg","panel","panel2","text","dim","accent","accent-rgb","secondary","secondary-rgb","edge","edge-faint"].forEach(function(k){
      colors["--"+k]=style.getPropertyValue("--"+k).trim();
    });
    api.sendHost("editor-state:"+JSON.stringify({theme:theme,html:byId("layoutEditor").innerHTML,colors:colors}));
  }
  function editorCommand(command){
    if(!editing||!command)return false;
    var root=byId("layoutEditor"),node;
    if(command.action==="preset-name"){
      presetName=String(command.value||"").slice(0,48);return true;
    }
    if(command.action==="close"){requestCloseEdit();return true;}
    if(command.action==="window-close"){closeEditorWindow();return true;}
    if(command.action==="choice"){finishEdit(command.value==="yes");return true;}
    if(command.action==="button"){editorAction(command.value);return true;}
    var fields={field:"data-layout-field",range:"data-layout-range",color:"data-layout-color",text:"data-layout-text"};
    if(fields[command.action]){
      node=root.querySelector("["+fields[command.action]+'="'+String(command.key).replace(/[^a-zA-Z]/g,"")+'"]');
      if(!node)return false;
      node.value=String(command.value);
      if(command.action==="range"||command.action==="color")node.dispatchEvent(new Event("input",{bubbles:true}));
      if(!command.preview){node.dispatchEvent(new Event("change",{bubbles:true}));renderEditor();}
      return true;
    }
    if(command.action==="all"){applyEveryTheme=command.value===true;return true;}
    return false;
  }
  function escapeHtml(s){return String(s).replace(/[&<>"']/g,function(c){return {"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c];});}
  function editorAction(cmd){
    if(cmd==="library-prev"){presetPage=Math.max(0,presetPage-1);return renderEditor();}
    if(cmd==="library-next"){presetPage=Math.min(Math.ceil(presetLibrary.length/12)-1,presetPage+1);return renderEditor();}
    if(cmd==="library-save")return saveDraftToLibrary();
    if(cmd==="library-export"){
      var snapshot=shareSnapshot();if(!snapshot)return;
      if(api.live)api.sendHost("preset-export:"+JSON.stringify(snapshot));
      else downloadDemoPreset(snapshot);
      return;
    }
    if(cmd==="library-import"){
      if(api.live)api.sendHost("preset-import");
      else api.toast("실제 격납고에서 공유 파일 불러오기를 사용할 수 있습니다.");
      return;
    }
    if(/^library-(apply|export|delete)-[a-f\d]{32}$/i.test(cmd)){
      var parts=cmd.match(/^library-(apply|export|delete)-([a-f\d]{32})$/i);
      var id=parts[2],action=parts[1],item=presetLibrary.find(function(p){return p.id===id;});
      if(!item)return;
      if(action==="apply")return applyStoredPreset(id);
      if(action==="export"){
        if(api.live)api.sendHost("preset-export-stored:"+id);
        else downloadDemoPreset({format:"campaign-hangar-ui",version:1,
          name:item.name,theme:item.theme,layout:item.layout});
        return;
      }
      if(api.live)api.sendHost("preset-delete:"+id);
      else if(window.confirm("‘"+item.name+"’을 보관함에서 삭제하시겠습니까?")){
        presetLibrary=presetLibrary.filter(function(p){return p.id!==id;});
        persistDemoPresetLibrary();renderEditor();
      }
      return;
    }
    if(cmd==="palettes"){palettesOpen=!palettesOpen;return renderEditor();}
    if(cmd.indexOf("color-reset-")===0){
      var key=cmd.slice(12);if(COLOR_KEYS.indexOf(key)<0)return;
      record();delete draft.colors[key];apply();return renderEditor();
    }
    if(/^palette-\d+$/.test(cmd)){
      var p=COLOR_COMBINATIONS[Number(cmd.slice(8))];if(!p)return;
      record();draft.colors=cleanColors({bg:p[1],panel:p[2],panelEnd:p[1],menu:p[1],menuEnd:p[2],
        list:p[1],listEnd:p[2],detail:p[2],detailEnd:p[1],accent:p[3],secondary:p[4],
        text:p[5],titleText:p[5],buttonText:p[5],mutedText:p[6],menuIcon:p[3],iconColor:p[4],
        borderColor:p[3],selectedColor:p[3],progressColor:p[4]});
      apply();return renderEditor();
    }
    if(cmd==="free")return change("free",!draft.free);
    if(cmd.indexOf("hide-")===0||cmd.indexOf("restore-")===0){
      var hide=cmd.indexOf("hide-")===0,id=cmd.slice(hide?5:8);
      if(FREE_IDS.indexOf(id)<0)return;
      record();draft.hiddenElements=draft.hiddenElements.filter(function(k){return k!==id;});
      if(hide)draft.hiddenElements.push(id);
      apply();syncEditorHandles();return renderEditor();
    }
    if(cmd==="cancel")return requestCloseEdit();
    if(cmd==="apply")return finishEdit(true);
    if(cmd==="reset"){record();draft=preset(theme,"base");apply();syncEditorHandles();return renderEditor();}
    if(cmd==="reset-colors"){record();draft.colors={};apply();return renderEditor();}
    if(cmd==="undo"&&undo.length){redo.push(clone(draft));draft=undo.pop();apply();syncEditorHandles();return renderEditor();}
    if(cmd==="redo"&&redo.length){undo.push(clone(draft));draft=redo.pop();apply();syncEditorHandles();return renderEditor();}
  }
  function downloadDemoPreset(preset){
    var data=new Blob([JSON.stringify(preset,null,2)],{type:"application/json"});
    var url=URL.createObjectURL(data),link=document.createElement("a");
    link.href=url;link.download="CampaignHangar-UI.chui.json";link.click();
    setTimeout(function(){URL.revokeObjectURL(url);},0);
  }
  function setupCampaignVisibility(){
    document.querySelectorAll(".side-nav [data-view]").forEach(function(node){
      if(CAMPAIGN_MENU_IDS.indexOf(node.dataset.view)<0)return;
      var trigger=document.createElement("span");
      trigger.className="nav-visibility-toggle";
      trigger.setAttribute("role","button");trigger.tabIndex=0;
      node.appendChild(trigger);
      function toggle(e){
        e.preventDefault();e.stopPropagation();
        if(!editing)return;
        var id=node.dataset.view;
        record();
        var hidden=(draft.hiddenCampaigns||[]).slice(),idx=hidden.indexOf(id);
        if(idx<0)hidden.push(id);else hidden.splice(idx,1);
        draft.hiddenCampaigns=hidden;apply();
      }
      trigger.addEventListener("click",toggle);
      trigger.addEventListener("keydown",function(e){
        if(e.key==="Enter"||e.key===" "){toggle(e);}
      });
    });
  }
  function setDraggables(on){
    document.querySelectorAll(".side-nav [data-view]").forEach(function(n){
      // The protected editor entry is not a campaign menu item.
      var canMove=on&&CAMPAIGN_MENU_IDS.indexOf(n.dataset.view)>=0;
      n.draggable=canMove;n.classList.toggle("layout-drag-item",canMove);
    });
    document.querySelectorAll("#hero,.action-strip,#catalogPanel").forEach(function(n){
      var enabled=on&&!(draft&&draft.free);n.draggable=enabled;n.classList.toggle("layout-drag-item",enabled);
    });
    ACTION_IDS.forEach(function(id){var n=byId(id);if(n){n.draggable=on;n.classList.toggle("layout-drag-item",on);}});
    document.querySelectorAll(".contact-cell").forEach(function(n){
      n.draggable=on&&draft&&draft.presentation==="contact"&&!draft.free;
      n.classList.toggle("layout-drag-item",n.draggable);
    });
  }
  function intersects(a,b,gap){gap=gap||8;return a.x<b.x+b.w+gap &&
    a.x+a.w+gap>b.x && a.y<b.y+b.h+gap && a.y+a.h+gap>b.y;}
  function overlapsFree(id,rect){
    return visibleFreeIds(draft).some(function(other){return other!==id &&
      !(other==="detail"&&draft.detail==="hidden") &&
      intersects(rect,draft.panels[other],8);});
  }
  // Keep the dragged position first. Move other, unlocked panels out of its
  // way; shrink a panel only when a nearby full-size position is unavailable.
  // Locked panels are inserted first and are never moved or resized.
  function findFreePlacement(id,wanted,occupied,isTarget){
    var preferred=cleanRect(wanted,wanted,id),minimum=FREE_MIN[id],best=null,bestCost=Infinity;
    if(!occupied.some(function(p){return intersects(preferred,p,8);}))return preferred;
    [1,.9,.8,.7,.6,0].forEach(function(scale){
      var w=Math.ceil(Math.max(minimum[0],Math.round(preferred.w*scale/10)*10)/10)*10,
        h=Math.ceil(Math.max(minimum[1],Math.round(preferred.h*scale/10)*10)/10)*10,
        maxX=1000-w,maxY=FREE_CANVAS_LENGTH-h;
      var xs=[preferred.x,0,maxX],ys=[preferred.y,0,maxY];
      occupied.forEach(function(p){
        xs.push(p.x+p.w+10,p.x-w-10,p.x);
        ys.push(p.y+p.h+10,p.y-h-10,p.y);
      });
      // Sparse candidates keep pointer movement responsive over a long canvas.
      for(var x=0;x<=maxX;x+=100)xs.push(x);
      for(var y=0;y<=maxY;y+=120)ys.push(y);
      var validX=Array.from(new Set(xs.map(function(x){return Math.max(0,Math.min(maxX,Math.round(x/10)*10));})));
      var validY=Array.from(new Set(ys.map(function(y){return Math.max(0,Math.min(maxY,Math.round(y/10)*10));})));
      var shrink=(1-w*h/(preferred.w*preferred.h))*(isTarget?1200:720);
      validX.forEach(function(x){validY.forEach(function(y){
        var cost=Math.abs(x-preferred.x)+Math.abs(y-preferred.y)+shrink;
        if(cost>=bestCost)return;
        var candidate={x:x,y:y,w:w,h:h,locked:preferred.locked};
        if(occupied.some(function(p){return intersects(candidate,p,8);}))return;
        best=candidate;bestCost=cost;
      });});
    });
    return best;
  }
  function reflowFreePanels(id,wanted,baseline){
    if(!baseline || !baseline.panels || baseline.panels[id].locked)return null;
    var shown=visibleFreeIds(baseline);
    function pack(keepUnchanged){
      var next=clone(baseline.panels),occupied=[];
      shown.forEach(function(k){if(k!==id&&next[k].locked)occupied.push(next[k]);});
      var placed=findFreePlacement(id,wanted,occupied,true);
      if(!placed)return null;
      next[id]=placed;occupied.push(placed);
      var others=shown.filter(function(k){return k!==id&&!next[k].locked;});
      if(keepUnchanged){
        var unaffected=others.filter(function(k){return !occupied.some(function(p){return intersects(next[k],p,8);});});
        // Avoid accepting overlaps left by an older saved layout.
        unaffected=unaffected.filter(function(k,i){return !unaffected.slice(0,i).some(function(previous){
          return intersects(next[k],next[previous],8);
        });});
        unaffected.forEach(function(k){occupied.push(next[k]);});
        others=others.filter(function(k){return unaffected.indexOf(k)<0;});
      }
      others.sort(function(a,b){return next[b].w*next[b].h-next[a].w*next[a].h;});
      for(var i=0;i<others.length;i++){
        var key=others[i],candidate=findFreePlacement(key,next[key],occupied,false);
        if(!candidate)return null;
        next[key]=candidate;occupied.push(candidate);
      }
      if(shown.some(function(a,i){return shown.slice(i+1).some(function(b){
        return intersects(next[a],next[b],8);
      });}))return null;
      return next;
    }
    return pack(true)||pack(false);
  }
  function setFreePanel(id,patch){
    if(!editing||!draft.free||FREE_IDS.indexOf(id)<0||draft.panels[id].locked)return;
    var wanted=Object.assign({},draft.panels[id],patch),placed=reflowFreePanels(id,wanted,draft);
    if(!placed){api.toast("고정된 패널을 보호하면서 배치할 수 없습니다. 고정을 해제하거나 크기를 조절해 주세요.");return renderEditor();}
    if(JSON.stringify(placed)===JSON.stringify(draft.panels))return renderEditor();
    record();draft.panels=placed;apply();renderEditor();
  }
  function removeFreeHandles(){
    document.querySelectorAll(".layout-free-handle,.layout-free-edge,.layout-menu-resize").forEach(function(n){n.remove();});
  }
  function syncEditorHandles(){
    setDraggables(!!(editing&&draft&&!draft.free));removeResizeGrips();removeFreeHandles();
    if(!editing)return;
    if(draft.free)addFreeHandles();else addResizeGrips();
  }
  function beginFreePointer(id,kind,e,node){
    if(!editing||!draft.free||document.body.dataset.layoutFree!=="true"||e.button!==0||
       draft.panels[id].locked)return;
    e.preventDefault();e.stopPropagation();activeFreePanel=id;
    moving={id:id,kind:kind,x:e.clientX,y:e.clientY,
      rect:clone(draft.panels[id]),before:clone(draft)};
    try{node.setPointerCapture(e.pointerId);}catch(_){}
  }
  function updateFreePointer(id,e){
    if(!moving||moving.id!==id)return;
    var area=byId("workspace");
    if(!area.clientWidth||!area.clientHeight)return;
    var dx=Math.round((e.clientX-moving.x)*1000/area.clientWidth/10)*10;
    var dy=Math.round((e.clientY-moving.y)*1000/area.clientHeight/10)*10;
    var wanted=clone(moving.rect),kind=moving.kind;
    if(kind==="move"){wanted.x+=dx;wanted.y+=dy;}
    else {
      if(kind.indexOf("w")>=0){wanted.x+=dx;wanted.w-=dx;}
      if(kind.indexOf("e")>=0)wanted.w+=dx;
      if(kind.indexOf("n")>=0){wanted.y+=dy;wanted.h-=dy;}
      if(kind.indexOf("s")>=0)wanted.h+=dy;
    }
    var packed=reflowFreePanels(id,wanted,moving.before);
    draft.panels=packed||clone(moving.before.panels);
    applyFreeGeometry(draft);
  }
  function endFreePointer(id){
    if(!moving||moving.id!==id)return;
    var before=moving.before,placed=reflowFreePanels(id,draft.panels[id],before);
    if(!placed){draft=before;api.toast("다른 패널과 겹칩니다. 위치나 크기를 조절해 주세요.");}
    else draft.panels=placed;
    if(JSON.stringify(draft)!==JSON.stringify(before)){
      undo.push(before);if(undo.length>32)undo.shift();redo=[];
    }
    moving=null;apply();renderEditor();
  }
  function addFreeHandles(){
    visibleFreeIds(draft).forEach(function(id){
      var node=freeNode(id);if(!node)return;
      if(!node.dataset.freePointerBound){
        node.dataset.freePointerBound="true";
        node.addEventListener("pointerdown",function(e){
          if(!editing||!draft||!draft.free||e.button!==0)return;
          var edge=e.target.closest("[data-free-edge]");
          if(!edge&&e.target.closest("button,a,input,select,textarea,[role=button],.layout-grip"))return;
          var kind=edge?edge.dataset.freeEdge:"move";
          beginFreePointer(id,kind,e,node);
        });
        node.addEventListener("pointermove",function(e){updateFreePointer(id,e);});
        node.addEventListener("pointerup",function(){endFreePointer(id);});
        node.addEventListener("pointercancel",function(){endFreePointer(id);});
      }
      var lock=document.createElement("button");
      lock.type="button";lock.className="layout-free-handle layout-free-lock";
      lock.textContent=draft.panels[id].locked?"🔒":"🔓";
      lock.title=draft.panels[id].locked?"패널 고정 해제":"패널 위치·크기 고정";
      lock.setAttribute("aria-label",id+" "+lock.title);
      lock.setAttribute("aria-pressed",String(!!draft.panels[id].locked));
      node.appendChild(lock);
      lock.addEventListener("click",function(e){
        e.preventDefault();e.stopPropagation();activeFreePanel=id;
        record();draft.panels[id].locked=!draft.panels[id].locked;
        syncEditorHandles();apply();renderEditor();
      });
      var close=document.createElement("button");
      close.type="button";close.className="layout-free-handle layout-free-close";close.textContent="×";
      close.title=ELEMENT_LABELS[id]+" 숨기기";close.setAttribute("aria-label",close.title);
      close.addEventListener("click",function(e){e.preventDefault();e.stopPropagation();editorAction("hide-"+id);});
      node.appendChild(close);
      if(FREE_ACTION_IDS.indexOf(id)>=0){
        var grip=document.createElement("span");grip.className="layout-free-handle layout-free-move";
        grip.textContent="⠿";grip.title="작업 버튼 이동";node.appendChild(grip);
        grip.addEventListener("pointerdown",function(e){beginFreePointer(id,"move",e,node);});
      }
      if(draft.panels[id].locked)return;
      ["n","ne","e","se","s","sw","w","nw"].forEach(function(dir){
        var edge=document.createElement("span");
        edge.className="layout-free-edge layout-free-edge-"+dir;
        edge.dataset.freeEdge=dir;edge.setAttribute("role","separator");
        edge.setAttribute("aria-label","패널 "+dir+" 테두리 크기 조절");
        node.appendChild(edge);
      });
    });
    var sidebar=document.querySelector(".sidebar"),grip=document.createElement("span");
    grip.className="layout-menu-resize";grip.setAttribute("role","separator");
    grip.setAttribute("aria-label","메뉴 테두리를 끌어 너비 조절");grip.title="메뉴 테두리 조절";
    sidebar.appendChild(grip);
    var menuMove=null;
    grip.addEventListener("pointerdown",function(e){
      if(e.button!==0||!draft||!draft.free)return;
      e.preventDefault();e.stopPropagation();
      menuMove={x:e.clientX,y:e.clientY,before:clone(draft),size:draft.menuSize};
      try{grip.setPointerCapture(e.pointerId);}catch(_){}
    });
    grip.addEventListener("pointermove",function(e){
      if(!menuMove)return;
      var side=draft.menu==="left"||draft.menu==="right";
      var delta=side?e.clientX-menuMove.x:e.clientY-menuMove.y;
      if(draft.menu==="right"||draft.menu==="bottom")delta=-delta;
      draft.menuSize=clamp(menuMove.size+delta,side?170:70,340,menuMove.size);
      document.body.style.setProperty(side?"--ui-menu-width":"--ui-menu-height",draft.menuSize+"px");
    });
    function endMenuMove(){
      if(!menuMove)return;
      if(draft.menuSize!==menuMove.size){undo.push(menuMove.before);redo=[];}
      menuMove=null;apply();renderEditor();
    }
    grip.addEventListener("pointerup",endMenuMove);
    grip.addEventListener("pointercancel",endMenuMove);
  }
  function visibleFreeIds(v){
    return FREE_IDS.filter(function(id){
      if((v.hiddenElements||[]).indexOf(id)>=0||id==="actions")return false;
      if(ACTION_IDS.indexOf(id)>=0)return true;
      if(id.indexOf("contact-")===0)return v.presentation==="contact";
      if(v.presentation==="contact")return id==="header";
      if(id==="hero"&&v.presentation==="list")return false;
      if(id==="detail"&&(v.detail==="hidden"||v.presentation==="focus"))return false;
      return true;
    });
  }
  function addResizeGrips(){
    [["hero","heroHeight"],["detailPanel","detailWidth"],["catalogPanel","listHeight"]].forEach(function(p){
      var n=byId(p[0]);if(n.querySelector(".layout-grip"))return;
      var grip=document.createElement("span");grip.className="layout-grip";grip.textContent="◢";
      grip.title="마우스로 크기 조절";grip.setAttribute("role","separator");
      grip.setAttribute("aria-label",p[1]+" 크기 조절");n.appendChild(grip);
      grip.addEventListener("pointerdown",function(e){
        if(!editing)return;e.preventDefault();e.stopPropagation();
        resizing={field:p[1],x:e.clientX,y:e.clientY,base:clone(draft)};
        try{grip.setPointerCapture(e.pointerId);}catch(_){}
      });
      grip.addEventListener("pointermove",function(e){
        if(!resizing||resizing.field!==p[1])return;
        var delta=p[1]==="detailWidth"?-1*(e.clientX-resizing.x):(e.clientY-resizing.y);
        var v=clone(resizing.base);v[p[1]]+=delta;draft=sanitize(v,theme);apply();
      });
      grip.addEventListener("pointerup",function(){
        if(!resizing)return;
        if(JSON.stringify(draft)!==JSON.stringify(resizing.base)){undo.push(resizing.base);redo=[];}
        resizing=null;renderEditor();
      });
    });
  }
  function removeResizeGrips(){document.querySelectorAll(".layout-grip").forEach(function(e){e.remove();});}
  function onDragStart(e){
    if(!editing)return;
    var button=e.target.closest(".side-nav .nav-campaign[data-view]"),
        action=e.target.closest("#launchButton,#installButton,#detailButton,#screenshotButton,#importZipButton"),
        panel=e.target.closest("#hero,.action-strip,#catalogPanel"),
        contact=e.target.closest(".contact-cell");
    if(button)dragging={type:"nav",id:button.dataset.view};
    else if(action)dragging={type:"actions",id:action.id};
    else if(panel&&!draft.free)dragging={type:"parts",id:panel.id==="hero"?"hero":panel.id==="catalogPanel"?"catalog":"actions"};
    else if(contact&&draft.presentation==="contact"&&!draft.free)
      dragging={type:"contactOrder",id:contact.dataset.contactCell};
    else return;
    e.dataTransfer.effectAllowed="move";e.dataTransfer.setData("text/plain",dragging.id);
  }
  function onDragOver(e){
    if(!editing||!dragging)return;
    var target=dragging.type==="nav"?e.target.closest(".side-nav .nav-campaign[data-view]"):
      dragging.type==="actions"?e.target.closest("#launchButton,#installButton,#detailButton,#screenshotButton,#importZipButton"):
      dragging.type==="contactOrder"?e.target.closest(".contact-cell"):
      e.target.closest("#hero,.action-strip,#catalogPanel");
    if(target){e.preventDefault();e.dataTransfer.dropEffect="move";target.classList.add("layout-drop-over");}
  }
  function onDrop(e){
    if(!editing||!dragging)return;
    var target=dragging.type==="nav"?e.target.closest(".side-nav .nav-campaign[data-view]"):
      dragging.type==="actions"?e.target.closest("#launchButton,#installButton,#detailButton,#screenshotButton,#importZipButton"):
      dragging.type==="contactOrder"?e.target.closest(".contact-cell"):
      e.target.closest("#hero,.action-strip,#catalogPanel");
    if(target){
      e.preventDefault();var dest=dragging.type==="nav"?target.dataset.view:
        dragging.type==="actions"?target.id:
        dragging.type==="contactOrder"?target.dataset.contactCell:
        target.id==="hero"?"hero":target.id==="catalogPanel"?"catalog":"actions";
      if(dest!==dragging.id){var list=draft[dragging.type].slice(),a=list.indexOf(dragging.id),b=list.indexOf(dest);
        if(a>=0&&b>=0){record();list.splice(a,1);list.splice(b,0,dragging.id);
          draft[dragging.type]=dragging.type==="nav"?validNav(list):list;
          apply();renderEditor();}
      }
    }
    dragging=null;document.querySelectorAll(".layout-drop-over").forEach(function(n){n.classList.remove("layout-drop-over");});
  }
  function scheduleDock(){
    if(archiveSuspended)return;
    if(!api||!api.live||api.getState().view!=="archive"){lastDock="";return;}
    if(dockFrame)return;
    dockFrame=requestAnimationFrame(function(){
      dockFrame=0;var node=byId("archiveMount");
      if(!node||node.offsetParent===null)return;
      var rect=node.getBoundingClientRect(),x={x:Math.round(rect.left),y:Math.round(rect.top),
        width:Math.round(rect.width),height:Math.round(rect.height),
        viewWidth:window.innerWidth,viewHeight:window.innerHeight};
      var json=JSON.stringify(x);
      if(json!==lastDock){lastDock=json;api.sendHost("archive-rect:"+json);}
    });
  }
  function onView(view){
    if(!api)return;
    var grid=byId("contactGrid"),v=current();
    if(grid){grid.hidden=v.presentation!=="contact"||view==="archive"||view==="settings";
      if(!grid.hidden&&api.renderContacts)api.renderContacts();}
    applyFreeGeometry(current());
    if(view==="archive")scheduleDock();
    else if(api.live){lastDock="";api.sendHost("archive-hide");}
  }
  function suspendArchive(){
    if(!api || !api.live)return;
    archiveSuspended=true;lastDock="";
    api.sendHost("archive-suspend");
  }
  function resumeArchive(){
    archiveSuspended=false;lastDock="";
    if(api&&api.live){
      api.sendHost("archive-resume");
      if(api.getState().view==="archive")scheduleDock();
    }
  }
  function onTheme(id){
    if(THEME_IDS.indexOf(id)<0)return;
    if(editing&&theme!==id)cancelEdit();
    theme=id;ensureProfiles();apply();
  }
  function onSelection(_){ /* selection is read from the canonical campaign state */ }
  function openManage(c){
    if(!c)return api.toast("캠페인을 선택해 주세요.");
    byId("manageTitle").textContent=c.name;
    byId("manageCategory").textContent=c.category+" · "+c.kind+" · "+c.author;
    byId("manageDescription").textContent=c.desc;
    byId("manageState").textContent=c.installed?"설치됨 · 기존 격납고 관리 기능 연결":"미설치 · 설치·연결 절차 사용";
    if(api.safeCover)api.safeCover("manageCover",c.img);
    byId("manageOverlay").dataset.campaignId=c.id;
    byId("manageActionPanel").hidden=true;
    byId("manageOverlay").querySelector(".manage-actions").hidden=false;
    byId("manageOverlay").querySelectorAll("[data-manage-action]").forEach(function(button){
      var action=button.dataset.manageAction;
      if(action==="install")button.hidden=c.installed;
      button.disabled=(action==="remove"||action==="versions"||action==="records"||action==="update"||action==="apply")&&!c.installed;
      if(action==="install")button.innerHTML=(api.icon?api.icon("install"):"")+"<span>설치</span>";
    });
    byId("manageOverlay").hidden=false;byId("manageClose").focus();
  }
  function closeManage(){
    byId("manageOverlay").hidden=true;
    byId("manageOverlay").dataset.campaignId="";
    byId("manageEmbeddedOptions").hidden=true;
    pendingManageAction="";manageChoiceState=null;
  }
  var manageOptions={
    install:["설치·연결","이 캠페인의 설치 정보 또는 전용 실행 경로를 확인합니다. 다음 단계에서 기존 관리 절차로 연결되며 선택만으로 파일은 변경되지 않습니다.","연결 옵션 열기","⌁"],
    update:["업데이트","등록된 배포처와 설치 버전을 확인합니다. 업데이트 파일 설치는 이후 확인 절차에서만 진행됩니다.","업데이트 옵션 열기","⇡"],
    remove:["설치 제거","캠페인과 설치 자료를 제거할 범위를 선택합니다. 이 단계에서는 삭제하지 않으며, 기존 제거 확인 절차가 한 번 더 표시됩니다.","제거 옵션 열기","×"],
    versions:["버전·폴더 관리","보관된 캠페인 버전을 확인하고 활성화 또는 폴더 작업을 선택할 수 있습니다. 적용·삭제는 다음 단계에서 확인합니다.","버전 관리 열기","▤"],
    link:["캠페인 연결","기존 설치본과 아카이브 등록 정보를 연결합니다. 실제 연결 대상은 다음 단계에서 선택합니다.","연결 대상 선택","◇"]
  };
  var pendingManageAction="",manageChoiceState=null,removalChoice="",removalConfirmed=false;
  function showManageAction(action){
    if(action==="update"){
      var updateId=byId("manageOverlay").dataset.campaignId;
      if(updateId&&api.live)api.sendHost("manage-action:update:"+updateId);
      closeManage();
      return;
    }
    // Installed CCM campaigns used to open the optimized-mode SitePicker
    // when the user chose Install/Connect. Keep the chooser in this theme.
    if(action==="install"){
      var id=byId("manageOverlay").dataset.campaignId;
      var selected=api.campaigns&&api.campaigns().find(function(c){return c.id===id;});
      if(selected&&selected.installed&&!selected.dedicated)action="link";
    }
    var a=manageOptions[action];if(!a)return;
    pendingManageAction=action;
    byId("manageActionTitle").textContent=a[0];
    byId("manageActionHelp").textContent=a[1];
    byId("manageActionConfirm").textContent=a[2];
    byId("manageActionSymbol").innerHTML=api.icon?api.icon(action==="update"?"reload":action==="link"?"external":action):"";
    byId("manageActionConfirm").hidden=(action==="versions"||action==="remove"||action==="link");
    byId("manageEmbeddedOptions").hidden=true;
    byId("manageEmbeddedOptions").innerHTML="";
    manageChoiceState=null;removalChoice="";removalConfirmed=false;
    byId("manageOverlay").querySelector(".manage-actions").hidden=true;
    byId("manageActionPanel").hidden=false;
    if(action==="versions"||action==="remove"||action==="link"){
      byId("manageActionHelp").textContent="설치 정보에서 선택 가능한 관리 작업을 확인하고 있습니다.";
      if(api.live)api.sendHost("manage-prepare:"+action+":"+byId("manageOverlay").dataset.campaignId);
      byId("manageActionBack").focus();
    }else byId("manageActionConfirm").focus();
  }
  function sendManageFinal(action,extra){
    var id=byId("manageOverlay").dataset.campaignId;
    if(!api.live||!id||!action)return;
    // The native host validates the campaign ID, permitted operation, and
    // stored version key anew; these values are untrusted UI selections.
    api.sendHost("manage-final:"+JSON.stringify({id:id,action:action,
      versionKey:extra&&extra.versionKey||"",removalKind:extra&&extra.removalKind||"",
      siteId:extra&&extra.siteId||"",mismatchAcknowledged:!!(extra&&extra.mismatchAcknowledged)}));
    closeManage();
  }
  function setManageChoices(action,data){
    if(!data||!manageOptions[action]||action!==pendingManageAction||
      byId("manageOverlay").hidden)return;
    manageChoiceState=data;
    var region=byId("manageEmbeddedOptions");
    if(action==="versions"){
      var values=Array.isArray(data.versions)?data.versions:[];
      byId("manageActionHelp").textContent="선택한 캠페인의 실제 보관 버전을 관리합니다. 폴더 열기는 파일을 변경하지 않습니다. 버전 적용은 기존 적용 확인 절차를 따릅니다.";
      region.innerHTML=values.length?'<label>보관 버전 선택<select id="manageVersionChoice">'+
        values.map(function(v,i){return '<option value="'+i+'">'+escapeHtml(v.label||v.versionKey||"버전")+'</option>';}).join("")+
        '</select></label><div class="manage-choice-actions">'+
        '<button type="button" id="manageOpenFolder">폴더 열기</button>'+
        '<button type="button" id="manageApplyVersion">선택 버전 적용</button></div>':
        '<p class="manage-choice-empty">관리할 보관 버전이 없습니다.</p>';
      region.hidden=false;
      if(values.length){
        byId("manageOpenFolder").addEventListener("click",function(){
          var value=values[Number(byId("manageVersionChoice").value)];
          if(value)sendManageFinal("version-folder",{versionKey:value.versionKey});
        });
        byId("manageApplyVersion").addEventListener("click",function(){
          var value=values[Number(byId("manageVersionChoice").value)];
          if(value)sendManageFinal("version-apply",{versionKey:value.versionKey});
        });
      }
    }else if(action==="link"){
      var sites=Array.isArray(data.sites)?data.sites:[];
      if(data.kind==="external"){
        region.innerHTML='<p class="manage-choice-empty">외부 캠페인은 연결된 실행 경로와 제작자 런처 설정을 편집합니다. 기존 Windows 경로 선택 단계가 이어집니다.</p>';
        region.hidden=false;
        byId("manageActionConfirm").hidden=false;
        byId("manageActionConfirm").textContent="외부 연결 설정 열기";
        return;
      }
      byId("manageActionHelp").textContent="현재 설치본에 연결할 아카이브 캠페인을 선택합니다. 선택한 항목의 대상 게임이 다르면 별도 확인을 요구합니다.";
      region.innerHTML='<div class="manage-site-recommendations"><strong>연관 캠페인 추천</strong>'+
        '<div id="manageSiteRecommendations" class="manage-site-cards"></div></div>'+
        '<label class="manage-site-search">캠페인 검색<input type="search" id="manageSiteSearch" placeholder="아카이브 캠페인 이름 검색" autocomplete="off"></label>'+
        '<div id="manageSiteResults" class="manage-site-cards manage-site-results"></div>'+
        '<label class="manage-site-legacy-select" hidden>선택한 사이트 항목<select id="manageSiteChoice" size="5"></select></label>'+
        '<label class="manage-mismatch"><input type="checkbox" id="manageSiteMismatch"> 설치 대상이 다른 항목도 연결하는 것을 확인했습니다.</label>';
      region.hidden=false;
      var search=byId("manageSiteSearch"),choice=byId("manageSiteChoice"),check=byId("manageSiteMismatch");
      var recommendations=byId("manageSiteRecommendations"),results=byId("manageSiteResults");
      function siteCard(s){
        var src=s.thumbnail&&/^https:\/\//i.test(s.thumbnail)?s.thumbnail:"";
        return '<button class="manage-site-card" type="button" data-site-id="'+escapeHtml(s.id)+'"'+
          ' aria-pressed="'+(choice.value===s.id)+'">'+
          (src?'<img alt="" loading="lazy" referrerpolicy="no-referrer" src="'+escapeHtml(src)+'">':
            '<span class="manage-site-placeholder" aria-hidden="true">▧</span>')+
          '<span><strong>'+escapeHtml(s.name)+'</strong><small>'+
          (s.matchSlot?'설치 대상 일치':'설치 대상 다름 · 확인 필요')+'</small></span></button>';
      }
      function bindSiteCards(root){
        root.querySelectorAll('[data-site-id]').forEach(function(card){
          card.addEventListener("click",function(){
            choice.value=card.dataset.siteId;check.checked=false;updateSiteSelection();
          });
        });
      }
      function refreshSites(){
        var needle=search.value.trim().toLocaleLowerCase("ko");
        var filtered=sites.filter(function(s){return !needle||
          siteSearchMatches(s,needle);});
        var previouslySelected=choice.value||data.selectedId;
        choice.innerHTML=sites.map(function(s){return '<option value="'+escapeHtml(s.id)+'"'+
          (s.id===data.selectedId?' selected':'')+'>'+escapeHtml(s.name+(s.matchSlot?'':' · 대상 다름'))+'</option>';}).join("");
        if(previouslySelected&&sites.some(function(s){return s.id===previouslySelected;}))
          choice.value=previouslySelected;
        else if(choice.options.length&&!choice.value)choice.selectedIndex=0;
        var recommended=sites.filter(function(s){return s.recommended===true;}).slice(0,8);
        recommendations.innerHTML=recommended.length?recommended.map(siteCard).join(""):
          '<p class="manage-choice-empty">정확히 일치하는 추천이 없습니다. 아래에서 검색하세요.</p>';
        results.innerHTML=filtered.length?filtered.map(siteCard).join(""):
          '<p class="manage-choice-empty">일치하거나 가까운 이름이 없습니다.</p>';
        bindSiteCards(recommendations);bindSiteCards(results);
        updateSiteSelection();
      }
      function updateSiteSelection(){
        var selected=sites.find(function(s){return s.id===choice.value;});
        var valid=!!selected&&(selected.matchSlot||check.checked);
        byId("manageActionConfirm").hidden=false;
        byId("manageActionConfirm").disabled=!valid;
        byId("manageActionConfirm").textContent="선택한 사이트 항목 연결";
        check.parentElement.hidden=!selected||selected.matchSlot;
        region.querySelectorAll('[data-site-id]').forEach(function(card){
          card.setAttribute("aria-pressed",String(card.dataset.siteId===choice.value));
        });
      }
      search.addEventListener("input",refreshSites);
      search.addEventListener("focus",refreshSites);
      choice.addEventListener("change",function(){check.checked=false;updateSiteSelection();});
      check.addEventListener("change",updateSiteSelection);
      refreshSites();
    }else if(action==="remove"){
      var isLocal=data.kind!=="dedicated"&&data.kind!=="external";
      byId("manageActionHelp").textContent="삭제 대상 범위를 선택하세요. 삭제 요청 전 한 번 더 확인하며, 뱅크·세이브는 기존 안전 규칙대로 보존됩니다.";
      var choices=isLocal?[
        ["PreviousVersions","이전 버전만 정리","선택·최신·적용 중인 버전은 유지합니다."],
        ["CampaignOnly","선택 버전만 제거","다른 버전·ZIP·뱅크·세이브는 유지합니다."],
        ["AllInformation","보관 자료 전체 정리","버전·확인된 ZIP·연결 정보를 제거합니다. 뱅크·세이브는 보존하며 별도 경고를 표시합니다."]
      ]:[["Dedicated","설치·연결 항목 제거","제작자 파일·다른 캠페인의 공용 자료·뱅크·세이브는 보호합니다."]];
      region.innerHTML='<fieldset class="manage-remove-choices"><legend>제거 범위</legend>'+
        choices.map(function(v){return '<label><input type="radio" name="manage-removal" value="'+v[0]+'">'+
          '<span><strong>'+escapeHtml(v[1])+'</strong><small>'+escapeHtml(v[2])+'</small></span></label>';}).join("")+
        '</fieldset><div id="manageRemoveWarning" class="manage-remove-warning" hidden></div>';
      region.hidden=false;
      region.querySelectorAll('[name="manage-removal"]').forEach(function(input){
        input.addEventListener("change",function(){
          removalChoice=this.value;removalConfirmed=false;
          byId("manageRemoveWarning").hidden=true;
          byId("manageActionConfirm").hidden=false;
          byId("manageActionConfirm").textContent="제거 범위 확인";
        });
      });
    }
  }
  function closeRecord(){
    byId("recordOverlay").hidden=true;byId("recordConfirm").hidden=true;
    activeRecordId="";
    if(byId("recordDetails"))byId("recordDetails").focus();
  }
  function openRecord(c){
    if(!c)return;
    activeRecordId=c.id;
    byId("recordTitle").textContent=c.name+" · 플레이 기록";
    var completed=Array.isArray(c.completedMissions)?c.completedMissions:[];
    var values=[
      ["플레이 시간",(Number(c.playSeconds||0)/3600).toFixed(1)+"시간"],
      ["관측 완료 임무",Number(c.missions||0)+"개"],
      ["완료 회차",Number(c.clears||0)+"회"],
      ["뱅크 연결",c.bankLinked?"연결됨":"연결되지 않음"],
      ["최근 임무",c.lastMission||"기록 없음"]
    ];
    var html='<div class="record-facts">'+values.map(function(p){
      return '<div><small>'+escapeHtml(p[0])+'</small><strong>'+escapeHtml(p[1])+'</strong></div>';
    }).join("")+'</div>';
    html+='<p class="record-method">플레이 시간은 격납고에서 관측한 게임 실행 시간입니다. 완료 임무는 연결된 기록을 바탕으로 하며, 뱅크 연결 전에는 완료 수치를 확인할 수 없습니다.</p>';
    if(completed.length)html+='<h3>뱅크 완료 임무</h3><div class="record-missions">'+
      completed.map(function(m){return '<span>'+escapeHtml(m)+'</span>';}).join("")+'</div>';
    if(c.bankError)html+='<p class="record-error">'+escapeHtml(c.bankError)+'</p>';
    byId("recordDetailsContent").innerHTML=html;
    byId("recordBankLink").disabled=!api.live;
    byId("recordNewRun").disabled=!api.live||!c.bankLinked||!c.needsReview;
    byId("recordConfirm").hidden=true;
    byId("recordOverlay").hidden=false;
    byId("recordClose").focus();
  }
  function setWorkState(active,title){
    if(!byId("busyOverlay"))return;
    if(workTimer){clearInterval(workTimer);workTimer=null;}
    if(workDelay){clearTimeout(workDelay);workDelay=null;}
    workActive=!!active;
    if(!active){byId("busyOverlay").hidden=true;return;}
    workStart=performance.now();
    byId("busyTitle").textContent=title||"캠페인 작업 중";
    byId("busyElapsed").textContent="진행 시간 0초";
    if(!byId("workBrowseToggle")||byId("workBrowseToggle").getAttribute("aria-pressed")==="true")return;
    // Do not flash a fake loading screen for fast non-blocking operations.
    workDelay=setTimeout(function(){
      workDelay=null;if(!workActive)return;
      byId("busyOverlay").hidden=false;
      workTimer=setInterval(function(){
        if(!workActive)return;
        byId("busyElapsed").textContent="진행 시간 "+
          Math.max(0,Math.floor((performance.now()-workStart)/1000))+"초";
      },250);
    },650);
  }
  function showHostSettings(prefs){
    if(!prefs)return;
    byId("hostGamePath").textContent=prefs.game||"미설정";
    byId("hostStoragePath").textContent=prefs.storage||"미설정";
    byId("hostDownloadPath").textContent=prefs.downloads||"미설정";
    ["hostApplyConfirmation","hostAutoApply","hostAutoRemove"].forEach(function(id,i){
      if(typeof prefs[["confirm","autoApply","autoRemove"][i]]==="boolean")
        byId(id).checked=prefs[["confirm","autoApply","autoRemove"][i]];
    });
  }
  function init(host){
    api=host;ensureProfiles();setupCampaignVisibility();
    if(!api.live)try{setPresetLibrary(JSON.parse(localStorage.getItem("campaign-hangar-custom-presets")||"[]"));}catch(_){}
    var quick=document.querySelector(".sidebar>.side-quick-actions");
    var first=document.querySelector(".sidebar>.archive-caption");
    if(quick&&first)first.parentNode.insertBefore(quick,first);
    if(!api.live)try{var stored=localStorage.getItem("campaign-hangar-theme-layouts");
      if(stored)applyHostProfiles(JSON.parse(stored));}catch(_){}
    document.querySelectorAll("[data-archive-action]").forEach(function(b) {
      b.addEventListener("click",function(){if(api.live)api.sendHost("archive-"+b.dataset.archiveAction);});
    });
    document.querySelectorAll("[data-host-action]").forEach(function(b) {
      b.addEventListener("click",function(){if(api.live)api.sendHost("host-setting-action:"+b.dataset.hostAction);});
    });
    [["hostApplyConfirmation","confirm"],["hostAutoApply","auto-apply"],["hostAutoRemove","auto-remove"]]
      .forEach(function(p){byId(p[0]).addEventListener("change",function(){
        if(api.live)api.sendHost("host-setting-toggle:"+p[1]+":"+(this.checked?"1":"0"));
      });});
    byId("manageClose").addEventListener("click",closeManage);
    byId("manageActionBack").addEventListener("click",function(){
      pendingManageAction="";byId("manageActionPanel").hidden=true;
      byId("manageOverlay").querySelector(".manage-actions").hidden=false;
      byId("manageClose").focus();
    });
    byId("manageActionConfirm").addEventListener("click",function(){
      var id=byId("manageOverlay").dataset.campaignId,action=pendingManageAction;
      if(!id||!api.live||!manageOptions[action])return;
      if(action==="remove"){
        if(!removalChoice)return;
        if(!removalConfirmed){
          var warning=byId("manageRemoveWarning");warning.hidden=false;
          warning.textContent="선택한 캠페인 설치 자료의 제거를 요청합니다. 되돌리기 어려울 수 있으므로 실제 삭제 범위를 다시 확인하세요.";
          removalConfirmed=true;byId("manageActionConfirm").textContent="제거 요청 최종 확인";
          return;
        }
        sendManageFinal("remove-scope",{removalKind:removalChoice});return;
      }
      if(action==="link"&&manageChoiceState&&manageChoiceState.kind!=="external"){
        var chosen=byId("manageSiteChoice"),found=(manageChoiceState.sites||[]).find(function(s){return s.id===chosen.value;});
        if(!found||(!found.matchSlot&&!byId("manageSiteMismatch").checked))return;
        sendManageFinal("link-site",{siteId:found.id,mismatchAcknowledged:!found.matchSlot});return;
      }
      closeManage();pendingManageAction="";
      api.sendHost("manage-action:"+action+":"+id);
    });
    byId("recordClose").addEventListener("click",closeRecord);
    byId("recordOverlay").addEventListener("click",function(e){if(e.target===this)closeRecord();});
    byId("recordBankLink").addEventListener("click",function(){
      if(activeRecordId&&api.live)api.sendHost("record-action:bank:"+activeRecordId);
    });
    byId("recordNewRun").addEventListener("click",function(){byId("recordConfirm").hidden=false;});
    byId("recordConfirmApply").addEventListener("click",function(){
      byId("recordConfirm").hidden=true;
      if(activeRecordId&&api.live)api.sendHost("record-action:new-run:"+activeRecordId);
    });
    byId("recordConfirmCancel").addEventListener("click",function(){byId("recordConfirm").hidden=true;});
    byId("manageOverlay").addEventListener("click",function(e){if(e.target===this)closeManage();});
    byId("manageOverlay").querySelectorAll("[data-manage-action]").forEach(function(b){
      b.addEventListener("click",function(){
        var id=byId("manageOverlay").dataset.campaignId,action=b.dataset.manageAction;
        if(!id||!api.live)return;
        showManageAction(action);
      });
    });
    document.addEventListener("keydown",function(e){
      if(e.key==="Escape"){
        if(!byId("recordOverlay").hidden){e.stopPropagation();closeRecord();}
        else if(!byId("manageOverlay").hidden){e.stopPropagation();closeManage();}
        else if(editing){
          e.stopPropagation();e.preventDefault();
          if(draft&&draft.free){finishEdit(false);return;}
          if(byId("layoutExitConfirm")&&!byId("layoutExitConfirm").hidden){
            byId("layoutExitConfirm").hidden=true;
            byId("layoutEditor").querySelector('[data-edit-action="cancel"]').focus();
          }else requestCloseEdit();
        }
      }else if(editing&&(e.ctrlKey||e.metaKey)&&!e.altKey&&
        !(e.target&&e.target.closest&&e.target.closest("input,textarea,select,[contenteditable]"))){
        if(e.key.toLowerCase()==="z"){e.preventDefault();editorAction(e.shiftKey?"redo":"undo");}
        if(e.key.toLowerCase()==="y"){e.preventDefault();editorAction("redo");}
      }
    });
    document.addEventListener("dragstart",onDragStart);
    document.addEventListener("dragover",onDragOver);
    document.addEventListener("dragleave",function(e){if(e.target.classList)e.target.classList.remove("layout-drop-over");});
    document.addEventListener("drop",onDrop);
    document.addEventListener("dragend",function(){dragging=null;document.querySelectorAll(".layout-drop-over").forEach(function(n){n.classList.remove("layout-drop-over");});});
    window.addEventListener("resize",function(){applyFreeGeometry(current());updateNavigationPresentation();scheduleDock();});
    if(window.ResizeObserver)new ResizeObserver(scheduleDock).observe(byId("archiveMount"));
    renderSettings();apply();
  }
  window.HangarFeatures={
    init:init,onTheme:onTheme,onView:onView,onSelection:onSelection,
    suspendArchive:suspendArchive,resumeArchive:resumeArchive,
    openManage:openManage,closeManage:closeManage,
    setManageChoices:setManageChoices,
    openRecord:openRecord,closeRecord:closeRecord,setWorkState:setWorkState,
    applyHostProfiles:applyHostProfiles,showHostSettings:showHostSettings,
    layoutSaved:function(ok,message){api.toast(message||(ok?"사용자 레이아웃을 저장했습니다.":"레이아웃을 저장하지 못했습니다."));},
    beginEdit:beginEdit,applyLayout:apply,
    handleBack:handleBack,
    getProfiles:function(){return clone(profiles);},
    getLayout:function(id){return clone(profiles[id||theme].layout);},
    getEditingLayout:function(){return editing?clone(draft):null;},
    setPresetLibrary:setPresetLibrary,applyImportedPreset:applyImportedPreset,
    presetFeedback:presetFeedback,getPresetLibrary:function(){return clone(presetLibrary);},
    isEditing:function(){return editing;},editorCommand:editorCommand,syncExternalEditor:syncExternalEditor
  };
  function siteSearchMatches(site,query){
    var normalized=function(s){return String(s||"").toLocaleLowerCase("ko").replace(/[\s_\-·]+/g,"");};
    var needle=normalized(query),name=normalized(site.name+" "+site.id+" "+(site.aliases||[]).join(" "));
    return name.indexOf(needle)>=0;
  }
})();
