/**
 * ProjectS realtime bug-log endpoint.
 *
 * Required Script Properties (Project Settings > Script properties):
 * - BUG_LOG_SHEET_ID: destination Google Spreadsheet ID
 * - BUG_LOG_SHARED_SECRET: the same temporary anti-spam value configured in Unity
 *
 * Deploy this script as a Web App that executes as the spreadsheet-owning account.
 * The project manifest sets the script time zone to Asia/Seoul, but all date formatting
 * below names the time zone explicitly so date-tab selection remains unambiguous.
 */

const KST_TIME_ZONE = 'Asia/Seoul';
const HEADER = [
  'Timestamp(KST)',
  'Source',
  'Category',
  'Severity',
  'Message',
  'StackTrace',
  'BuildId',
  'User',
];

const MAX_CELL_TEXT_LENGTH = 50000;

function doGet() {
  return jsonResponse({ ok: true, service: 'ProjectS realtime bug log' });
}

function doPost(e) {
  let request;

  try {
    const rawBody = e && e.postData ? e.postData.contents : '';
    request = JSON.parse(rawBody);
  } catch (error) {
    return jsonResponse({ ok: false, error: 'invalid_json' });
  }

  const properties = PropertiesService.getScriptProperties();
  const expectedSecret = properties.getProperty('BUG_LOG_SHARED_SECRET');
  const spreadsheetId = properties.getProperty('BUG_LOG_SHEET_ID');

  if (!expectedSecret || !spreadsheetId) {
    // Do not reveal which server-side property is absent to an unauthenticated caller.
    return jsonResponse({ ok: false, error: 'server_not_configured' });
  }

  if (!request || request.secret !== expectedSecret) {
    return jsonResponse({ ok: false, error: 'unauthorized' });
  }

  if (!Array.isArray(request.logs) || request.logs.length === 0) {
    return jsonResponse({ ok: false, error: 'logs_required' });
  }

  const lock = LockService.getScriptLock();
  try {
    lock.waitLock(10000);

    const spreadsheet = SpreadsheetApp.openById(spreadsheetId);
    const rowsByDate = groupRowsByKstDate(request.logs);
    let accepted = 0;

    Object.keys(rowsByDate).forEach((dateTabName) => {
      const rows = rowsByDate[dateTabName];
      const sheet = getOrCreateSheet(spreadsheet, dateTabName);
      sheet.getRange(sheet.getLastRow() + 1, 1, rows.length, HEADER.length).setValues(rows);
      accepted += rows.length;
    });

    return jsonResponse({ ok: true, accepted: accepted });
  } catch (error) {
    // Apps Script ContentService cannot set a non-200 status. Unity therefore also checks ok.
    return jsonResponse({ ok: false, error: 'write_failed' });
  } finally {
    if (lock.hasLock()) lock.releaseLock();
  }
}

function groupRowsByKstDate(logs) {
  const rowsByDate = {};

  logs.forEach((log) => {
    if (!log) return;

    const timestamp = new Date(Number(log.ts));
    if (Number.isNaN(timestamp.getTime())) return;

    const dateTabName = Utilities.formatDate(timestamp, KST_TIME_ZONE, 'yyyy-MM-dd');
    if (!rowsByDate[dateTabName]) rowsByDate[dateTabName] = [];

    rowsByDate[dateTabName].push([
      Utilities.formatDate(timestamp, KST_TIME_ZONE, 'yyyy-MM-dd HH:mm:ss.SSS'),
      cellText(log.source),
      cellText(log.category),
      cellText(log.severity),
      cellText(log.message),
      cellText(log.stack),
      cellText(log.buildId),
      cellText(log.user),
    ]);
  });

  return rowsByDate;
}

function getOrCreateSheet(spreadsheet, tabName) {
  let sheet = spreadsheet.getSheetByName(tabName);
  if (sheet) return sheet;

  sheet = spreadsheet.insertSheet(tabName);
  sheet.getRange(1, 1, 1, HEADER.length).setValues([HEADER]);
  sheet.setFrozenRows(1);
  sheet.getRange(1, 1, 1, HEADER.length).setFontWeight('bold');
  sheet.autoResizeColumns(1, HEADER.length);
  return sheet;
}

function cellText(value) {
  const text = value === null || value === undefined ? '' : String(value);
  return text.length > MAX_CELL_TEXT_LENGTH ? text.slice(0, MAX_CELL_TEXT_LENGTH) : text;
}

function jsonResponse(payload) {
  return ContentService
    .createTextOutput(JSON.stringify(payload))
    .setMimeType(ContentService.MimeType.JSON);
}
