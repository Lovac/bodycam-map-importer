// Browser side of the web importer: remembers this player's maps (IndexedDB) and saves the finished files to a
// folder the player picks (or as downloads). Never touches the game folder.
(function () {
  const DB = 'bcmi-web', STORE = 'kv';

  function db() {
    return new Promise((ok, bad) => {
      const r = indexedDB.open(DB, 1);
      r.onupgradeneeded = () => r.result.createObjectStore(STORE);
      r.onsuccess = () => ok(r.result);
      r.onerror = () => bad(r.error);
    });
  }
  async function put(key, value) {
    const d = await db();
    return new Promise((ok, bad) => {
      const t = d.transaction(STORE, 'readwrite');
      t.objectStore(STORE).put(value, key);
      t.oncomplete = () => ok(true);
      t.onerror = () => bad(t.error);
    });
  }
  async function get(key) {
    const d = await db();
    return new Promise((ok, bad) => {
      const t = d.transaction(STORE, 'readonly');
      const r = t.objectStore(STORE).get(key);
      r.onsuccess = () => ok(r.result);
      r.onerror = () => bad(r.error);
    });
  }

  window.bcmi = {
    // the remembered fake Paks folder: { "relative/path": base64 }
    saveState: async (json) => { await put('paks', json); return true; },
    loadState: async () => (await get('paks')) || '',

    canPickFolder: () => typeof window.showDirectoryPicker === 'function',

    // ask once for an output folder (the Desktop by default); kept for next time
    pickFolder: async () => {
      try {
        const h = await window.showDirectoryPicker({ id: 'bcmi-out', mode: 'readwrite', startIn: 'desktop' });
        await put('outdir', h);
        return h.name;
      } catch (e) { return ''; }
    },
    folderName: async () => {
      const h = await get('outdir');
      return h ? h.name : '';
    },

    // write one file into the chosen folder; returns '' or the reason it could not
    writeOne: async (name, data) => {
      const h = await get('outdir');
      if (!h) return 'no folder';
      try {
        if ((await h.queryPermission({ mode: 'readwrite' })) !== 'granted' &&
            (await h.requestPermission({ mode: 'readwrite' })) !== 'granted') return 'permission';
        const f = await h.getFileHandle(name, { create: true });
        const w = await f.createWritable();
        await w.write(data);
        await w.close();
        return '';
      } catch (e) { return String(e && e.message || e); }
    },

    // fallback (Firefox, or no folder): plain downloads
    download: (name, data) => {
      const url = URL.createObjectURL(new Blob([data], { type: 'application/octet-stream' }));
      const a = document.createElement('a');
      a.href = url; a.download = name;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    },

    // read a dropped/picked file as bytes for .NET
    readInput: async (inputId) => {
      const el = document.getElementById(inputId);
      if (!el || !el.files || !el.files.length) return null;
      const f = el.files[0];
      return { name: f.name, data: new Uint8Array(await f.arrayBuffer()) };
    },

    ask: (text) => window.confirm(text)
  };
})();
