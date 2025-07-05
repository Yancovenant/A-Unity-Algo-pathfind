async function refreshState() {
    const res = await fetch('/api/state');
    const data = await res.json();
    const div = document.getElementById('agent-list');
    div.innerHTML = '';
    for (const [id, info] of Object.entries(data.agents)) {
        const box = document.createElement('div');
        box.innerHTML = `<strong>${id}</strong> at (${info.pos}) - ${info.status}`;
        div.appendChild(box);
    }
}
refreshState();
setInterval(refreshState, 2000);
