/**
 * ProjectS 외부 에셋 Drive 배포 알림기
 *
 * 설정값은 코드가 아닌 Apps Script 프로젝트 속성에 저장한다.
 * - MANIFEST_FILE_ID: 팀원 런처가 읽는 manifest.json의 Drive 파일 ID
 * - DISCORD_WEBHOOK_URL: 알림을 보낼 Discord 채널 Webhook URL
 *
 * 5분 주기 checkForRelease() 트리거가 manifest.latestVersion 증가만 Discord에 알린다.
 * ZIP/스냅샷 업로드는 게시 전 중간 단계이므로 알리지 않는다.
 */

const PROPERTY_MANIFEST_FILE_ID = 'MANIFEST_FILE_ID';
const PROPERTY_DISCORD_WEBHOOK_URL = 'DISCORD_WEBHOOK_URL';
const PROPERTY_LAST_NOTIFIED_VERSION = 'LAST_NOTIFIED_VERSION';

/**
 * 최초 설정: 현재 Drive 최신 버전을 기준선으로 저장하고, 이후 버전부터 자동 알림한다.
 * 이미 올라간 현재 버전도 알리고 싶다면 notifyCurrentRelease()를 한 번 실행한다.
 */
function initializeNotifier() {
  const manifest = readManifest_();
  PropertiesService.getScriptProperties().setProperty(
    PROPERTY_LAST_NOTIFIED_VERSION,
    String(manifest.latestVersion));
  console.log(`기준선 설정 완료: v${manifest.latestVersion}`);
}

/** Apps Script 시간 기반 트리거용. Drive manifest의 새 버전만 Discord에 알린다. */
function checkForRelease() {
  const manifest = readManifest_();
  const properties = PropertiesService.getScriptProperties();
  const previous = Number(properties.getProperty(PROPERTY_LAST_NOTIFIED_VERSION) || '0');

  if (manifest.latestVersion <= previous) {
    return;
  }

  const packageInfo = manifest.packages.find(item => item.version === manifest.latestVersion);
  if (!packageInfo) {
    throw new Error(`manifest의 latestVersion(v${manifest.latestVersion}) 패키지를 찾지 못했습니다.`);
  }

  postReleaseToDiscord_(packageInfo, previous, false);
  // Discord가 성공 응답한 경우에만 기록한다. 실패하면 다음 실행에서 재시도한다.
  properties.setProperty(PROPERTY_LAST_NOTIFIED_VERSION, String(manifest.latestVersion));
}

/** 현재 최신 버전을 즉시 시험 알림한다. 성공해도 자동 알림 기준선은 바꾸지 않는다. */
function notifyCurrentRelease() {
  const manifest = readManifest_();
  const packageInfo = manifest.packages.find(item => item.version === manifest.latestVersion);
  if (!packageInfo) {
    throw new Error(`manifest의 latestVersion(v${manifest.latestVersion}) 패키지를 찾지 못했습니다.`);
  }

  postReleaseToDiscord_(packageInfo, manifest.latestVersion - 1, true);
}

/** 5분 주기 트리거를 하나만 만든다. 중복 트리거는 먼저 정리한다. */
function installFiveMinuteTrigger() {
  ScriptApp.getProjectTriggers()
    .filter(trigger => trigger.getHandlerFunction() === 'checkForRelease')
    .forEach(trigger => ScriptApp.deleteTrigger(trigger));

  ScriptApp.newTrigger('checkForRelease')
    .timeBased()
    .everyMinutes(5)
    .create();
}

function readManifest_() {
  const properties = PropertiesService.getScriptProperties();
  const manifestFileId = requiredProperty_(properties, PROPERTY_MANIFEST_FILE_ID);
  const text = DriveApp.getFileById(manifestFileId).getBlob().getDataAsString('UTF-8');
  const manifest = JSON.parse(text);

  if (manifest.schemaVersion !== 2 || !Number.isInteger(manifest.latestVersion)
    || !Array.isArray(manifest.packages)) {
    throw new Error('manifest.json 형식이 올바르지 않습니다. schemaVersion 2 파일인지 확인하세요.');
  }

  return manifest;
}

function postReleaseToDiscord_(packageInfo, previousVersion, isTest) {
  const properties = PropertiesService.getScriptProperties();
  const webhookUrl = requiredProperty_(properties, PROPERTY_DISCORD_WEBHOOK_URL);
  const removedCount = Array.isArray(packageInfo.removedPaths) ? packageInfo.removedPaths.length : 0;
  const title = isTest ? '🧪 ProjectS 외부 에셋 알림 시험' : '📦 ProjectS 외부 에셋 패치 배포';
  const content = [
    `**${title}**`,
    `버전: **v${packageInfo.version}** (이전 v${previousVersion})`,
    `패키지: \`${packageInfo.name}\``,
    `종류: ${packageInfo.type === 'base' ? 'Base' : 'Patch'}`,
    removedCount > 0 ? `삭제 파일: ${removedCount}개` : null,
    isTest ? '※ 시험 메시지입니다. 팀원 설치는 필요하지 않습니다.' : 'ProjectSLauncher에서 업데이트 확인 후 설치하세요.',
  ].filter(Boolean).join('\n');

  const response = UrlFetchApp.fetch(webhookUrl, {
    method: 'post',
    contentType: 'application/json',
    payload: JSON.stringify({ content }),
    muteHttpExceptions: true,
  });

  const status = response.getResponseCode();
  if (status < 200 || status >= 300) {
    throw new Error(`Discord 알림 전송 실패: HTTP ${status} ${response.getContentText()}`);
  }
}

function requiredProperty_(properties, key) {
  const value = properties.getProperty(key);
  if (!value) {
    throw new Error(`스크립트 속성 '${key}'를 먼저 설정하세요.`);
  }

  return value;
}
