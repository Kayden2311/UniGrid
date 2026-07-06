<<<<<<< HEAD
﻿const button =
    document.getElementById("ai-button");

const windowDiv =
    document.getElementById("ai-chat-window");

button.addEventListener("click", () => {

    if (windowDiv.style.display === "none")
        windowDiv.style.display = "block";
    else
        windowDiv.style.display = "none";
});
=======
﻿(function () {
    "use strict";

    // ── Config ──────────────────────────────────────────────
    const API_URL = "/api/assistant/ask";

    // ── DOM refs ────────────────────────────────────────────
    const button    = document.getElementById("ai-button");
    const chatWin   = document.getElementById("ai-chat-window");
    const messagesEl= document.getElementById("ai-messages");
    const input     = document.getElementById("ai-input");
    const sendBtn   = document.getElementById("ai-send");
    const closeBtn  = document.getElementById("ai-close");

    // ── Conversation history (role/content pairs for API) ───
    let history = [];

    // ── Toggle open/close ───────────────────────────────────
    button.addEventListener("click", () => chatWin.classList.toggle("open"));
    closeBtn.addEventListener("click", () => chatWin.classList.remove("open"));

    // ── Auto-resize textarea ────────────────────────────────
    input.addEventListener("input", () => {
        input.style.height = "auto";
        input.style.height = Math.min(input.scrollHeight, 100) + "px";
    });

    // ── Send on Enter (Shift+Enter = newline) ───────────────
    input.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            handleSend();
        }
    });
    sendBtn.addEventListener("click", handleSend);

    // ── Core send logic ─────────────────────────────────────
    async function handleSend() {
        const text = input.value.trim();
        if (!text) return;

        input.value = "";
        input.style.height = "auto";
        setLoading(true);

        appendMessage("user", text);

        const typingEl = appendTyping();

        try {
            const reply = await sendToApi(text);
            typingEl.remove();
            appendMessage("bot", reply);
            history.push({ role: "user", content: text });
            history.push({ role: "assistant", content: reply });
        } catch (err) {
            typingEl.remove();
            appendMessage("bot", "Sorry, something went wrong. Please try again.");
            console.error("[assistant]", err);
        } finally {
            setLoading(false);
            input.focus();
        }
    }

    async function sendToApi(message) {
        const res = await fetch(API_URL, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ message, history }),
        });

        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        return data.reply;
    }

    // ── DOM helpers ─────────────────────────────────────────
    function appendMessage(role, text) {
        const wrap   = document.createElement("div");
        wrap.className = `ai-msg ai-msg--${role === "user" ? "user" : "bot"}`;

        const bubble = document.createElement("div");
        bubble.className = "ai-bubble";
        bubble.textContent = text;

        wrap.appendChild(bubble);
        messagesEl.appendChild(wrap);
        scrollToBottom();
        return wrap;
    }

    function appendTyping() {
        const wrap   = document.createElement("div");
        wrap.className = "ai-msg ai-msg--bot ai-typing";

        const bubble = document.createElement("div");
        bubble.className = "ai-bubble";
        bubble.innerHTML = '<span class="ai-dot"></span><span class="ai-dot"></span><span class="ai-dot"></span>';

        wrap.appendChild(bubble);
        messagesEl.appendChild(wrap);
        scrollToBottom();
        return wrap;
    }

    function setLoading(loading) {
        sendBtn.disabled = loading;
        input.disabled   = loading;
    }

    function scrollToBottom() {
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }
})();
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
