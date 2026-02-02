; Kam AI Voice Assistant Installer
; NSIS Installer Script
; Build with: makensis Kam-Installer.nsi

!include "MUI2.nsh"
!include "LogicLib.nsh"

; Product Information
!define PRODUCT_NAME "Kam"
!define PRODUCT_FULL_NAME "Kam - AI Voice Assistant"
!define PRODUCT_VERSION "1.0.0"
!define PRODUCT_PUBLISHER "KAM Neural Core"
!define PRODUCT_WEB_SITE "https://github.com/Esquetta/Kam"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\Kam.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"
!define PRODUCT_STARTMENU_REGVAL "NSIS:StartMenuDir"

; Installer Settings
Name "${PRODUCT_FULL_NAME}"
OutFile "..\artifacts\Kam-${PRODUCT_VERSION}-Setup.exe"
InstallDir "$PROGRAMFILES64\Kam"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel admin
SetCompressor lzma

; Interface Settings
!define MUI_ABORTWARNING
!define MUI_ICON "..\src\Ui\SmartVoiceAgent.Ui\Assets\favicon.ico"
!define MUI_UNICON "..\src\Ui\SmartVoiceAgent.Ui\Assets\favicon.ico"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU Application $ICONS_GROUP
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Kam.exe"
!define MUI_FINISHPAGE_RUN_CHECKED
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_INSTFILES

; Languages
!insertmacro MUI_LANGUAGE "English"

; Installer Sections
Section "Main Application" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer
  
  ; Published files
  File /r "..\publish\*.*"
  
  ; Create Start Menu shortcuts
  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
  CreateDirectory "$SMPROGRAMS\$ICONS_GROUP"
  CreateShortcut "$SMPROGRAMS\$ICONS_GROUP\Kam.lnk" "$INSTDIR\Kam.exe"
  CreateShortcut "$SMPROGRAMS\$ICONS_GROUP\Uninstall.lnk" "$INSTDIR\uninst.exe"
  !insertmacro MUI_STARTMENU_WRITE_END
  
  ; Create Desktop shortcut
  CreateShortcut "$DESKTOP\Kam.lnk" "$INSTDIR\Kam.exe"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\uninst.exe"
  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\Kam.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_FULL_NAME}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\Kam.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

; Uninstaller Sections
Section Uninstall
  !insertmacro MUI_STARTMENU_GETFOLDER Application $ICONS_GROUP
  
  ; Remove shortcuts
  Delete "$SMPROGRAMS\$ICONS_GROUP\Kam.lnk"
  Delete "$SMPROGRAMS\$ICONS_GROUP\Uninstall.lnk"
  Delete "$DESKTOP\Kam.lnk"
  RMDir "$SMPROGRAMS\$ICONS_GROUP"
  
  ; Remove registry keys
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
  
  ; Remove files and directories
  RMDir /r "$INSTDIR"
SectionEnd

; Function to check if already installed
Function .onInit
  ${If} ${Silent}
    SetAutoClose true
  ${EndIf}
  
  ; Check for existing installation
  ReadRegStr $R0 HKLM "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $R0 "" done
  
  MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
    "Kam is already installed. $\n$\nClick `OK` to remove the previous version or `Cancel` to cancel this upgrade." \
    IDOK uninst
  Abort
  
uninst:
  ExecWait '$R0 /S _?=$INSTDIR'
  
done:
FunctionEnd
