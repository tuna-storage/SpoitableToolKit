// **正は C#（AppUpdates）** — ここは配られた様子（appUpdate）を描いて、押されたものを返すだけ。
// シェルの本体より先に読む（本体の boot と push の口がここの関数を呼ぶ）。中で使う bridge・t・el などは本体のもの。

let appUpdateView = null;

/** 「更新を確認」を押したか。インストールした版でないときの一言は、押したときだけ出す。 */
let appUpdateAsked = false;

function applyAppUpdate(view) {
  if (!view) return;
  appUpdateView = view;
  renderUpdateBanner();
  renderUpdateRow();
}

function updateTitle(view) {
  return t(view.downgrade ? 'host.update.switch' : 'host.update.available', view.version);
}

/** バナー。**ほかのバナー（読み込めなかったモジュールなど）とは別の 1 枚**で、いつも先頭に置く。 */
function renderUpdateBanner() {
  const view = appUpdateView;
  let node = document.getElementById('updateBanner');
  if (!view?.banner || !view.version) {
    node?.remove();
    return;
  }

  if (!node) {
    node = document.createElement('div');
    node.id = 'updateBanner';
    node.className = 'banner banner-update';
    node.setAttribute('role', 'status');
  }
  if (el.banners.firstChild !== node) el.banners.prepend(node);

  const square = document.createElement('span');
  square.className = 'sq';
  square.dataset.tone = 'accent';
  square.appendChild(icon('i-download'));

  const text = document.createElement('div');
  text.className = 'text';
  text.append(cell('title', updateTitle(view)));
  const note = view.phase === 'downloading' ? t('host.update.downloading', view.percent ?? 0)
    : view.phase === 'restarting' ? t('host.update.restarting')
    : view.phase === 'installFailed' ? t('host.update.installFailed')
    : '';
  const body = cell('body', note);
  body.hidden = !note;
  text.append(body);

  const acts = document.createElement('div');
  acts.className = 'acts';
  if (view.phase !== 'downloading' && view.phase !== 'restarting') {
    if (view.hasNotes) acts.append(updateButton('updateChanges', t('host.update.changes'), openUpdateNotes));
    acts.append(updateButton('updateInstall', t('host.update.install'), installAppUpdate, true));
    acts.append(updateButton('updateSkip', t('host.update.skip'), skipAppUpdate));
  }

  node.replaceChildren(square, text, acts);
}

function updateButton(id, label, onClick, primary = false) {
  const button = document.createElement('button');
  button.type = 'button';
  button.id = id;
  button.className = primary ? 'spoitable-btn spoitable-btn-small spoitable-btn-primary' : 'spoitable-btn spoitable-btn-small';
  button.textContent = label;
  button.addEventListener('click', onClick);
  return button;
}

async function installAppUpdate() {
  applyAppUpdate(await bridge.installAppUpdate());
}

async function skipAppUpdate() {
  applyAppUpdate(await bridge.skipAppUpdate());
}

/** **字として入れる**（HTML としては読まない）。 */
async function openUpdateNotes() {
  const notes = await bridge.getAppUpdateNotes();
  const modal = document.getElementById('updateNotesModal');
  document.getElementById('updateNotesTitle').textContent = t('host.update.changesTitle', notes.version ?? '');

  const body = document.getElementById('updateNotesBody');
  body.replaceChildren(...notesBlocks(notes.blocks ?? []));
  modal.hidden = false;
  body.scrollTop = 0;
  document.getElementById('updateNotesClose').focus();
}

function closeUpdateNotes() {
  document.getElementById('updateNotesModal').hidden = true;
}

function notesBlocks(blocks) {
  const nodes = [];
  let list = null;
  for (const block of blocks) {
    if (block.kind === 'item') {
      if (!list) {
        list = document.createElement('ul');
        nodes.push(list);
      }
      const item = document.createElement('li');
      item.textContent = block.text;
      list.appendChild(item);
      continue;
    }

    list = null;
    const node = document.createElement(block.kind === 'heading' ? (block.level <= 1 ? 'h3' : 'h4') : 'p');
    node.textContent = block.text;
    nodes.push(node);
  }
  return nodes;
}

function addUpdateRow(section) {
  const anchor = section.querySelector('.cfg-item[data-key="CheckAppUpdates"]')?.closest('.cfg-box');
  if (!anchor) return;

  const row = document.createElement('div');
  row.className = 'cfg-item cfg-update';
  row.id = 'updateRow';

  const meta = document.createElement('div');
  meta.className = 'meta';
  meta.append(cell('name', ''), cell('desc', ''));

  const check = document.createElement('button');
  check.type = 'button';
  check.id = 'updateCheck';
  check.className = 'spoitable-btn spoitable-btn-small';
  check.textContent = t('host.update.check');
  check.addEventListener('click', checkForAppUpdate);
  const control = document.createElement('div');
  control.className = 'ctl';
  control.appendChild(check);

  row.append(meta, control);

  // 設定の行と同じ包み（探すときの当たり・畳みの動き）に入れる。
  const wrap = document.createElement('div');
  wrap.className = 'cfg-row';
  const clip = document.createElement('div');
  clip.className = 'cfg-clip';
  clip.appendChild(row);
  wrap.appendChild(clip);
  anchor.appendChild(wrap);
  renderUpdateRow(row);
}

/** 描いている途中の段（まだ画面に付いていない）でも描けるよう、行を受け取れる。 */
function renderUpdateRow(row = document.getElementById('updateRow')) {
  const view = appUpdateView;
  if (!row || !view) return;

  row.querySelector('.name').textContent = t('host.update.version', view.currentVersion);
  const status = !view.installed ? (appUpdateAsked ? t('host.update.notInstalled') : '')
    : view.phase === 'checking' ? t('host.update.checking')
    : view.phase === 'upToDate' ? t('host.update.upToDate')
    : view.phase === 'checkFailed' ? t('host.update.checkFailed')
    : view.version ? updateTitle(view)
    : '';
  const desc = row.querySelector('.desc');
  desc.textContent = status;
  desc.hidden = !status;

  const check = row.querySelector('#updateCheck');
  check.disabled = view.phase === 'checking' || view.phase === 'downloading' || view.phase === 'restarting';
}

async function checkForAppUpdate() {
  appUpdateAsked = true;
  if (appUpdateView?.installed) applyAppUpdate({ ...appUpdateView, phase: 'checking' });
  applyAppUpdate(await bridge.checkAppUpdate());
}

async function startAppUpdates() {
  const modal = document.getElementById('updateNotesModal');
  document.getElementById('updateNotesClose').addEventListener('click', closeUpdateNotes);
  modal.addEventListener('mousedown', event => { if (event.target === modal) closeUpdateNotes(); });

  applyAppUpdate(await bridge.getAppUpdate());
}
