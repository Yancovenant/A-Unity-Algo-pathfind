// monitor.js: Live video for YOLO Monitor
(function() {
  // Map agentId -> canvas context
  const canvases = {};
  document.querySelectorAll('canvas[id^="canvas-"]').forEach(canvas => {
    const agentId = canvas.id.replace('canvas-', '');
    canvases[agentId] = canvas.getContext('2d');
  });

  // Connect to backend WebSocket
  const ws = new WebSocket('ws://' + window.location.host + '/ws/monitor');
  ws.binaryType = 'arraybuffer';

  ws.onmessage = function(event) {
    const arr = new Uint8Array(event.data);
    let newline = arr.indexOf(10); // '\n'
    if (newline === -1) return;
    const header = JSON.parse(new TextDecoder().decode(arr.slice(0, newline)));
    const agentId = header.agent_id;
    const imgData = arr.slice(newline + 1);
    const ctx = canvases[agentId];
    if (!ctx) return;
    const img = new Image();
    img.onload = function() {
      ctx.clearRect(0, 0, 160, 120);
      ctx.drawImage(img, 0, 0, 160, 120);
      URL.revokeObjectURL(img.src);
    };
    img.src = URL.createObjectURL(new Blob([imgData], {type: 'image/jpeg'}));
    //console.log("Received frame for agent", agentId, "size", imgData.length);
  };

  ws.onopen = function() {
    console.log('[Monitor] Connected to backend');
  };
  ws.onclose = function() {
    console.log('[Monitor] Disconnected from backend');
  };
})(); 