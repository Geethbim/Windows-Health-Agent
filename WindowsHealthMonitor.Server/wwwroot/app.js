function fmtPercent(v) {
  if (v === null || v === undefined) return 'n/a';
  return `${v.toFixed(1)}%`;
}

function fmtUptime(ts) {
  if (!ts) return 'n/a';
  // server serializes TimeSpan like "HH:MM:SS" by default; keep as-is
  return ts;
}

function setStatus(text) {
  document.getElementById('status').textContent = text;
}

function escapeHtml(s) {
  return String(s)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

async function refresh() {
  setStatus('Refreshing…');

  const [machinesRes, alertsRes] = await Promise.all([
    fetch('/api/machines'),
    fetch('/api/alerts')
  ]);

  const machines = await machinesRes.json();
  const alerts = await alertsRes.json();

  const machinesBody = document.getElementById('machines');
  machinesBody.innerHTML = '';

  for (const m of machines) {
    const disks = (m.disks || []).map(d => `${escapeHtml(d.name)}: ${d.freePercent.toFixed(1)}% free`).join('<br>') || '—';
    const services = (m.services || []).map(s => `${escapeHtml(s.name)}: ${escapeHtml(s.status)}`).join('<br>') || '—';
    const fans = (m.fans || []).map(f => {
      const hw = f.hardware ? ` (${escapeHtml(f.hardware)})` : '';
      return `${escapeHtml(f.name)}: ${Number(f.rpm).toFixed(0)} RPM${hw}`;
    }).join('<br>') || '—';
    const temps = (m.temperatures || []).map(t => {
      const hw = t.hardware ? ` (${escapeHtml(t.hardware)})` : '';
      return `${escapeHtml(t.name)}: ${Number(t.celsius).toFixed(1)}°C${hw}`;
    }).join('<br>') || '—';

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><code>${escapeHtml(m.machineName)}</code></td>
      <td>${escapeHtml(m.timestampUtc)}</td>
      <td>${escapeHtml(m.osDescription)}</td>
      <td>${escapeHtml(fmtUptime(m.uptime))}</td>
      <td>${escapeHtml(fmtPercent(m.cpuUsagePercent))}</td>
      <td>${escapeHtml(fmtPercent(m.memoryUsedPercent))}</td>
      <td>${disks}</td>
      <td>${services}</td>
      <td>${fans}</td>
      <td>${temps}</td>
    `;
    machinesBody.appendChild(tr);
  }

  const alertsBody = document.getElementById('alerts');
  alertsBody.innerHTML = '';

  for (const a of alerts) {
    const sev = String(a.severity || '').toLowerCase();
    const klass = (sev === 'critical') ? 'critical' : (sev === 'warning') ? 'warning' : 'ok';

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escapeHtml(a.timestampUtc)}</td>
      <td><code>${escapeHtml(a.machineName)}</code></td>
      <td><span class="badge ${klass}">${escapeHtml(a.severity)}</span></td>
      <td>${escapeHtml(a.code)}</td>
      <td>${escapeHtml(a.message)}</td>
    `;
    alertsBody.appendChild(tr);
  }

  setStatus(`Last refresh: ${new Date().toISOString()}`);
}

document.getElementById('refresh').addEventListener('click', () => refresh().catch(err => setStatus(`Error: ${err}`)));

refresh().catch(err => setStatus(`Error: ${err}`));
setInterval(() => refresh().catch(err => setStatus(`Error: ${err}`)), 5000);
