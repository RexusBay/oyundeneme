const elements = {
    setupPanel: document.getElementById("setupPanel"),
    gamePanel: document.getElementById("gamePanel"),
    nameInput: document.getElementById("nameInput"),
    roomInput: document.getElementById("roomInput"),
    createButton: document.getElementById("createButton"),
    joinButton: document.getElementById("joinButton"),
    roomCode: document.getElementById("roomCode"),
    copyButton: document.getElementById("copyButton"),
    statusText: document.getElementById("statusText"),
    turnText: document.getElementById("turnText"),
    players: document.getElementById("players"),
    board: document.getElementById("board"),
    restartButton: document.getElementById("restartButton"),
    leaveButton: document.getElementById("leaveButton")
};

const state = {
    socket: null,
    room: "",
    name: "",
    symbol: "",
    current: null
};

const symbolText = {
    A: "Alev",
    B: "Gunes"
};

const markText = {
    ".": "",
    A: "A",
    B: "B"
};

for (let index = 0; index < 25; index += 1) {
    const button = document.createElement("button");
    button.className = "cell";
    button.type = "button";
    button.dataset.index = String(index);
    button.addEventListener("click", () => playMove(index));
    elements.board.appendChild(button);
}

elements.createButton.addEventListener("click", () => connect(true));
elements.joinButton.addEventListener("click", () => connect(false));
elements.copyButton.addEventListener("click", copyInviteLink);
elements.restartButton.addEventListener("click", restartRound);
elements.leaveButton.addEventListener("click", leaveRoom);

bootstrapFromUrl();

function bootstrapFromUrl() {
    const params = new URLSearchParams(window.location.search);
    const room = (params.get("room") || "").toUpperCase();
    if (room) {
        elements.roomInput.value = room;
    }
}

function connect(createRoom) {
    const name = sanitizeName(elements.nameInput.value);
    const room = createRoom ? generateRoomCode() : sanitizeRoom(elements.roomInput.value);

    if (!name) {
        setStatus("Ismini yazman lazim.");
        return;
    }

    if (!room || room.length < 4) {
        setStatus("Gecerli bir oda kodu gir.");
        return;
    }

    if (state.socket) {
        state.socket.close();
    }

    state.name = name;
    state.room = room;
    elements.roomInput.value = room;
    elements.roomCode.textContent = room;
    setStatus("Odaya baglaniliyor...");
    showGame();

    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const url = `${protocol}//${window.location.host}/ws?room=${encodeURIComponent(room)}&name=${encodeURIComponent(name)}`;
    const socket = new WebSocket(url);
    state.socket = socket;

    socket.addEventListener("open", () => {
        const params = new URLSearchParams(window.location.search);
        params.set("room", room);
        window.history.replaceState({}, "", `${window.location.pathname}?${params.toString()}`);
    });

    socket.addEventListener("message", event => {
        const payload = JSON.parse(event.data);
        if (payload.type === "state") {
            state.current = payload.state;
            syncView();
        }
    });

    socket.addEventListener("close", () => {
        if (state.socket === socket) {
            setStatus("Baglanti kapandi. Yeniden katilabilirsin.");
        }
    });

    socket.addEventListener("error", () => {
        setStatus("Baglanti kurulamadi.");
    });
}

function playMove(index) {
    if (!state.current || !state.socket || state.socket.readyState !== WebSocket.OPEN) {
        return;
    }

    const me = findMe();
    if (!me) {
        return;
    }

    if (state.current.currentTurn !== me.symbol || state.current.winner) {
        return;
    }

    state.socket.send(JSON.stringify({ type: "move", cell: index }));
}

function restartRound() {
    if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
        return;
    }

    state.socket.send(JSON.stringify({ type: "restart" }));
}

function leaveRoom() {
    if (state.socket) {
        state.socket.close();
        state.socket = null;
    }

    state.room = "";
    state.current = null;
    showSetup();
    setStatus("");
    const params = new URLSearchParams(window.location.search);
    params.delete("room");
    const suffix = params.toString();
    window.history.replaceState({}, "", suffix ? `${window.location.pathname}?${suffix}` : window.location.pathname);
}

function syncView() {
    const current = state.current;
    if (!current) {
        return;
    }

    elements.roomCode.textContent = current.code;
    elements.statusText.textContent = current.status;

    const me = findMe();
    const currentTurnPlayer = current.players.find(player => player.symbol === current.currentTurn);
    const waitingForPlayer = current.players.length < 2;

    if (waitingForPlayer) {
        elements.turnText.textContent = "Ikinci oyuncu gelince oyun baslayacak.";
    } else if (current.winner === "D") {
        elements.turnText.textContent = "Bu raund berabere.";
    } else if (current.winner) {
        elements.turnText.textContent = `${current.players.find(player => player.symbol === current.winner)?.name || "Bir oyuncu"} kazandi.`;
    } else if (me && me.symbol === current.currentTurn) {
        elements.turnText.textContent = "Sira sende, bir kare sec.";
    } else {
        elements.turnText.textContent = `Sirada ${currentTurnPlayer?.name || "rakibin"} var.`;
    }

    renderPlayers(current, me);
    renderBoard(current, me, waitingForPlayer);
}

function renderPlayers(current, me) {
    elements.players.innerHTML = "";

    current.players.forEach(player => {
        const card = document.createElement("div");
        card.className = `player-card${current.currentTurn === player.symbol && !current.winner ? " active" : ""}`;

        const badge = document.createElement("div");
        badge.className = `player-badge ${player.symbol === "A" ? "badge-a" : "badge-b"}`;
        badge.textContent = player.symbol;

        const name = document.createElement("strong");
        name.textContent = me && me.name === player.name ? `${player.name} (sen)` : player.name;

        const role = document.createElement("p");
        role.textContent = `${symbolText[player.symbol]} taslari`;

        card.appendChild(badge);
        card.appendChild(name);
        card.appendChild(role);
        elements.players.appendChild(card);
    });

    if (current.players.length === 1) {
        const placeholder = document.createElement("div");
        placeholder.className = "player-card";
        placeholder.innerHTML = "<strong>Yer hazir</strong><p>Linki paylas, ikinci oyuncu katilsin.</p>";
        elements.players.appendChild(placeholder);
    }
}

function renderBoard(current, me, waitingForPlayer) {
    const canPlay = me && current.currentTurn === me.symbol && !current.winner && !waitingForPlayer;
    const cells = elements.board.querySelectorAll(".cell");

    cells.forEach((cell, index) => {
        const mark = current.board[index];
        cell.textContent = markText[mark];
        cell.className = `cell${mark === "." ? "" : ` ${mark.toLowerCase()}`}`;
        cell.disabled = mark !== "." || !canPlay;
    });
}

function findMe() {
    return state.current?.players.find(player => player.name === state.name) || null;
}

function copyInviteLink() {
    if (!state.room) {
        return;
    }

    const url = `${window.location.origin}${window.location.pathname}?room=${encodeURIComponent(state.room)}`;
    navigator.clipboard.writeText(url)
        .then(() => setStatus("Davet linki panoya kopyalandi."))
        .catch(() => setStatus(url));
}

function showGame() {
    elements.setupPanel.classList.add("hidden");
    elements.gamePanel.classList.remove("hidden");
}

function showSetup() {
    elements.setupPanel.classList.remove("hidden");
    elements.gamePanel.classList.add("hidden");
}

function setStatus(message) {
    elements.statusText.textContent = message;
}

function generateRoomCode() {
    const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    let room = "";
    for (let index = 0; index < 6; index += 1) {
        room += chars[Math.floor(Math.random() * chars.length)];
    }
    return room;
}

function sanitizeRoom(value) {
    return value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);
}

function sanitizeName(value) {
    return value.trim().replace(/[^\p{L}\p{N}\s]/gu, "").slice(0, 20);
}
