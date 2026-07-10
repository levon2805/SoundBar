/* ============================================
   SoundBar Remote — Client Application
   ============================================ */

(() => {
    'use strict';

    // --- State ---
    let ws = null;
    let isPaired = false;
    let reconnectAttempts = 0;
    const MAX_RECONNECT_DELAY = 10000;
    let sliderDebounceTimers = {};
    let lastState = null;

    // --- DOM References ---
    const pairingScreen = document.getElementById('pairing-screen');
    const appScreen = document.getElementById('app-screen');
    const pairingInput = document.getElementById('pairing-code');
    const pairBtn = document.getElementById('pair-btn');
    const pairingError = document.getElementById('pairing-error');
    const connectionStatus = document.getElementById('connection-status');
    const clientStatus = document.getElementById('client-status');
    const masterSlider = document.getElementById('master-slider');
    const masterValue = document.getElementById('master-value');
    const masterMuteBtn = document.getElementById('master-mute-btn');
    const nowPlayingCard = document.getElementById('now-playing-card');
    const albumArt = document.getElementById('album-art');
    const songTitle = document.getElementById('song-title');
    const songArtist = document.getElementById('song-artist');
    const seekSlider = document.getElementById('seek-slider');
    const currentTime = document.getElementById('current-time');
    const totalTime = document.getElementById('total-time');
    const prevBtn = document.getElementById('prev-btn');
    const playPauseBtn = document.getElementById('play-pause-btn');
    const nextBtn = document.getElementById('next-btn');
    const appsList = document.getElementById('apps-list');
    const deviceSelect = document.getElementById('device-select');

    // --- WebSocket Connection ---
    function connect() {
        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${location.host}/ws`;

        try {
            ws = new WebSocket(wsUrl);
        } catch (e) {
            scheduleReconnect();
            return;
        }

        ws.onopen = () => {
            reconnectAttempts = 0;
            updateConnectionStatus('connecting');
        };

        ws.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                handleMessage(msg);
            } catch (e) {
                console.error('Failed to parse message:', e);
            }
        };

        ws.onclose = () => {
            if (isPaired) {
                updateConnectionStatus('reconnecting');
            }
            scheduleReconnect();
        };

        ws.onerror = () => {
            // onclose will fire after this
        };
    }

    function scheduleReconnect() {
        reconnectAttempts++;
        const delay = Math.min(1000 * Math.pow(1.5, reconnectAttempts - 1), MAX_RECONNECT_DELAY);
        setTimeout(connect, delay);
    }

    function send(obj) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(obj));
        }
    }

    // --- Message Handling ---
    function handleMessage(msg) {
        switch (msg.type) {
            case 'pairingRequired':
                updateConnectionStatus('connected');
                break;

            case 'paired':
                if (msg.success) {
                    isPaired = true;
                    pairingError.textContent = '';
                    showScreen('app');
                } else {
                    pairingError.textContent = 'Incorrect code. Please try again.';
                    pairingInput.value = '';
                    pairingInput.focus();
                }
                break;

            case 'state':
                if (msg.data) {
                    updateUI(msg.data);
                    lastState = msg.data;
                }
                break;
        }
    }

    // --- UI Updates ---
    function updateUI(state) {
        // Master Volume
        if (!isSliderActive(masterSlider)) {
            masterSlider.value = state.masterVolume;
            updateSliderFill(masterSlider);
            masterValue.textContent = state.masterVolume + '%';
        }

        // Master Mute
        const muteIcon = masterMuteBtn.querySelector('.mute-icon');
        if (state.masterMuted) {
            masterMuteBtn.classList.add('muted');
            muteIcon.textContent = '🔇';
        } else {
            masterMuteBtn.classList.remove('muted');
            muteIcon.textContent = state.masterVolume > 50 ? '🔊' : state.masterVolume > 0 ? '🔉' : '🔈';
        }

        // Now Playing
        if (state.nowPlaying && state.nowPlaying.title) {
            nowPlayingCard.classList.remove('hidden');
            songTitle.textContent = state.nowPlaying.title;
            songArtist.textContent = state.nowPlaying.artist || '';

            // Album Art
            if (state.nowPlaying.albumArtBase64) {
                albumArt.src = 'data:image/jpeg;base64,' + state.nowPlaying.albumArtBase64;
                albumArt.classList.add('visible');
            } else {
                albumArt.classList.remove('visible');
            }

            // Seek slider
            if (!isSliderActive(seekSlider) && state.nowPlaying.durationSeconds > 0) {
                const pct = (state.nowPlaying.positionSeconds / state.nowPlaying.durationSeconds) * 100;
                seekSlider.value = pct;
                updateSliderFill(seekSlider);
                currentTime.textContent = formatTime(state.nowPlaying.positionSeconds);
                totalTime.textContent = formatTime(state.nowPlaying.durationSeconds);
            }

            // Play/Pause icon
            playPauseBtn.textContent = state.nowPlaying.isPlaying ? '⏸' : '▶️';
        } else {
            nowPlayingCard.classList.add('hidden');
        }

        // Apps
        updateAppsList(state.apps || []);

        // Devices
        updateDeviceList(state.devices || [], state.selectedDeviceId);
    }

    function updateAppsList(apps) {
        if (apps.length === 0) {
            appsList.innerHTML = '<div class="empty-state">No active audio applications</div>';
            return;
        }

        // Build or update rows efficiently
        const existingRows = appsList.querySelectorAll('.app-row');
        const existingMap = {};
        existingRows.forEach(row => {
            existingMap[row.dataset.processName] = row;
        });

        const newProcessNames = new Set(apps.map(a => a.rawProcessName));

        // Remove rows that no longer exist
        existingRows.forEach(row => {
            if (!newProcessNames.has(row.dataset.processName)) {
                row.remove();
            }
        });

        // Remove empty state if present
        const emptyState = appsList.querySelector('.empty-state');
        if (emptyState) emptyState.remove();

        apps.forEach(app => {
            let row = existingMap[app.rawProcessName];

            if (!row) {
                row = createAppRow(app);
                appsList.appendChild(row);
            }

            // Update existing row
            const nameEl = row.querySelector('.app-name');
            if (nameEl) nameEl.textContent = app.name;

            const slider = row.querySelector('.volume-slider');
            if (slider && !isSliderActive(slider)) {
                slider.value = app.volume;
                updateSliderFill(slider);
            }

            const valueEl = row.querySelector('.volume-value');
            if (valueEl && !isSliderActive(slider)) {
                valueEl.textContent = app.volume + '%';
            }

            const muteBtn = row.querySelector('.app-mute-btn');
            if (muteBtn) {
                muteBtn.classList.toggle('muted', app.isMuted);
                muteBtn.textContent = app.isMuted ? '🔇' : '🔊';
            }

            // Update icon
            const iconEl = row.querySelector('.app-icon');
            if (iconEl && app.iconBase64 && !iconEl.src.includes(app.iconBase64.substring(0, 32))) {
                iconEl.src = 'data:image/png;base64,' + app.iconBase64;
                iconEl.style.display = 'block';
                const placeholder = row.querySelector('.app-icon-placeholder');
                if (placeholder) placeholder.style.display = 'none';
            }
        });
    }

    function createAppRow(app) {
        const row = document.createElement('div');
        row.className = 'app-row';
        row.dataset.processName = app.rawProcessName;

        const hasIcon = app.iconBase64 && app.iconBase64.length > 0;

        row.innerHTML = `
            ${hasIcon
                ? `<img class="app-icon" src="data:image/png;base64,${app.iconBase64}" alt="${app.name}">`
                : `<div class="app-icon-placeholder">🎵</div><img class="app-icon" style="display:none" alt="${app.name}">`
            }
            <div class="app-info">
                <div class="app-name">${app.name}</div>
                <div class="app-slider-row">
                    <input type="range" class="volume-slider" min="0" max="100" value="${app.volume}" style="--fill: ${app.volume}%">
                    <span class="volume-value">${app.volume}%</span>
                </div>
            </div>
            <button class="app-mute-btn ${app.isMuted ? 'muted' : ''}" title="Mute">${app.isMuted ? '🔇' : '🔊'}</button>
        `;

        // Slider events
        const slider = row.querySelector('.volume-slider');
        const valueEl = row.querySelector('.volume-value');

        slider.addEventListener('input', () => {
            updateSliderFill(slider);
            valueEl.textContent = slider.value + '%';
            debounceSend('appVol_' + app.rawProcessName, () => {
                send({ action: 'setAppVolume', app: app.rawProcessName, value: parseInt(slider.value) });
            }, 30);
        });

        // Mute button
        const muteBtn = row.querySelector('.app-mute-btn');
        muteBtn.addEventListener('click', () => {
            const isMuted = !muteBtn.classList.contains('muted');
            send({ action: 'setAppMute', app: app.rawProcessName, boolValue: isMuted });
        });

        return row;
    }

    function updateDeviceList(devices, selectedId) {
        if (devices.length === 0) return;

        // Only rebuild if devices changed
        const currentOptions = Array.from(deviceSelect.options).map(o => o.value);
        const newIds = devices.map(d => d.id);

        if (JSON.stringify(currentOptions) !== JSON.stringify(newIds)) {
            deviceSelect.innerHTML = '';
            devices.forEach(device => {
                const option = document.createElement('option');
                option.value = device.id;
                option.textContent = device.name;
                deviceSelect.appendChild(option);
            });
        }

        if (selectedId && deviceSelect.value !== selectedId) {
            deviceSelect.value = selectedId;
        }
    }

    // --- Helper Functions ---
    function updateSliderFill(slider) {
        const pct = ((slider.value - slider.min) / (slider.max - slider.min)) * 100;
        slider.style.setProperty('--fill', pct + '%');
    }

    function formatTime(seconds) {
        if (!seconds || seconds < 0) return '0:00';
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    function isSliderActive(slider) {
        return slider.matches(':active') || document.activeElement === slider;
    }

    function debounceSend(key, fn, ms) {
        if (sliderDebounceTimers[key]) clearTimeout(sliderDebounceTimers[key]);
        sliderDebounceTimers[key] = setTimeout(fn, ms);
    }

    function showScreen(name) {
        pairingScreen.classList.toggle('active', name === 'pairing');
        appScreen.classList.toggle('active', name === 'app');
    }

    function updateConnectionStatus(status) {
        const dot = connectionStatus.querySelector('.status-dot');
        const text = connectionStatus.querySelector('span:last-child');

        switch (status) {
            case 'connected':
                dot.className = 'status-dot connected';
                text.textContent = 'Connected — enter code above';
                break;
            case 'connecting':
                dot.className = 'status-dot disconnected';
                text.textContent = 'Connecting...';
                break;
            case 'reconnecting':
                dot.className = 'status-dot disconnected';
                text.textContent = 'Reconnecting...';
                // Show reconnecting overlay or switch back to pairing
                if (isPaired) {
                    clientStatus.textContent = 'Reconnecting...';
                }
                break;
        }
    }

    // --- Event Listeners ---

    // Pairing
    pairingInput.addEventListener('input', () => {
        pairBtn.disabled = pairingInput.value.length !== 2;
        pairingError.textContent = '';
    });

    pairBtn.addEventListener('click', () => {
        send({ action: 'pair', pairingCode: pairingInput.value });
    });

    pairingInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && pairingInput.value.length === 2) {
            send({ action: 'pair', pairingCode: pairingInput.value });
        }
    });

    // Master Volume
    masterSlider.addEventListener('input', () => {
        updateSliderFill(masterSlider);
        masterValue.textContent = masterSlider.value + '%';
        debounceSend('master', () => {
            send({ action: 'setMasterVolume', value: parseInt(masterSlider.value) });
        }, 30);
    });

    masterMuteBtn.addEventListener('click', () => {
        const isMuted = !masterMuteBtn.classList.contains('muted');
        send({ action: 'setMasterMute', boolValue: isMuted });
    });

    // Media Controls
    prevBtn.addEventListener('click', () => send({ action: 'mediaPrevious' }));
    playPauseBtn.addEventListener('click', () => send({ action: 'mediaPlayPause' }));
    nextBtn.addEventListener('click', () => send({ action: 'mediaNext' }));

    seekSlider.addEventListener('change', () => {
        if (lastState && lastState.nowPlaying) {
            const seekSeconds = (seekSlider.value / 100) * lastState.nowPlaying.durationSeconds;
            send({ action: 'mediaSeek', value: seekSeconds });
        }
    });

    seekSlider.addEventListener('input', () => {
        updateSliderFill(seekSlider);
        if (lastState && lastState.nowPlaying) {
            const seekSeconds = (seekSlider.value / 100) * lastState.nowPlaying.durationSeconds;
            currentTime.textContent = formatTime(seekSeconds);
        }
    });

    // Device Switcher
    deviceSelect.addEventListener('change', () => {
        send({ action: 'setOutputDevice', deviceId: deviceSelect.value });
    });

    // --- Initialise ---
    // Initialise slider fills
    document.querySelectorAll('.volume-slider, .seek-slider').forEach(updateSliderFill);

    // Register Service Worker
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.register('/sw.js').catch(() => {});
    }

    // Connect!
    connect();
    pairingInput.focus();

})();
