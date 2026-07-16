(function () {
    "use strict";

    const API_URL = "/api/assistant/ask";
    const STORAGE_KEY = "unigrid.assistant.history.v1";
    const MAX_HISTORY_MESSAGES = 50;

    const button = document.getElementById("ai-button");
    const chatWin = document.getElementById("ai-chat-window");
    const messagesEl = document.getElementById("ai-messages");
    const input = document.getElementById("ai-input");
    const sendBtn = document.getElementById("ai-send");
    const closeBtn = document.getElementById("ai-close");

    if (!button || !chatWin || !messagesEl || !input || !sendBtn || !closeBtn) return;

    let history = loadHistory();
    history.forEach(message => appendMessage(message.role === "user" ? "user" : "bot", message.content));

    button.addEventListener("click", () => chatWin.classList.toggle("open"));
    closeBtn.addEventListener("click", () => chatWin.classList.remove("open"));

    input.addEventListener("input", () => {
        input.style.height = "auto";
        input.style.height = Math.min(input.scrollHeight, 100) + "px";
    });

    input.addEventListener("keydown", event => {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            handleSend();
        }
    });
    sendBtn.addEventListener("click", handleSend);

    async function handleSend() {
        const text = input.value.trim();
        if (!text) return;

        input.value = "";
        input.style.height = "auto";
        setLoading(true);
        appendMessage("user", text);

        const typingEl = appendTyping();
        try {
            const result = await sendToApi(text);
            typingEl.remove();
            appendMessage("bot", result.reply);

            history.push({ role: "user", content: text });
            history.push({ role: "assistant", content: result.reply });
            history = history.slice(-MAX_HISTORY_MESSAGES);
            saveHistory();

            if (result.dataChanged) {
                window.dispatchEvent(new CustomEvent("unigrid:schedule-changed", {
                    detail: { source: "assistant" }
                }));
            }
        } catch (error) {
            typingEl.remove();
            appendMessage("bot", "Xin lỗi, hiện không thể kết nối với trợ lý. Vui lòng thử lại.");
            console.error("[assistant]", error);
        } finally {
            setLoading(false);
            input.focus();
        }
    }

    async function sendToApi(message) {
        const response = await fetch(API_URL, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ message, history })
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const data = await response.json();
        return {
            reply: data.reply || "Không có phản hồi.",
            dataChanged: data.dataChanged === true
        };
    }

    function appendMessage(role, text) {
        const wrap = document.createElement("div");
        wrap.className = `ai-msg ai-msg--${role === "user" ? "user" : "bot"}`;

        const bubble = document.createElement("div");
        bubble.className = "ai-bubble";
        if (role === "user") {
            bubble.textContent = text;
        } else {
            bubble.classList.add("ai-markdown");
            bubble.innerHTML = renderSafeMarkdown(text);
        }

        wrap.appendChild(bubble);
        messagesEl.appendChild(wrap);
        scrollToBottom();
        return wrap;
    }

    function renderSafeMarkdown(value) {
        const escaped = String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");

        const renderInline = line => line
            .replace(/`([^`]+)`/g, "<code>$1</code>")
            .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
            .replace(/__([^_]+)__/g, "<strong>$1</strong>")
            .replace(/(^|\s)\*([^*]+)\*(?=\s|$|[.,!?])/g, "$1<em>$2</em>");

        return escaped.split(/\r?\n/).map(line => {
            const unordered = line.match(/^\s*[-*]\s+(.+)$/);
            if (unordered) {
                return `<div class="ai-md-list-item"><span aria-hidden="true">•</span><span>${renderInline(unordered[1])}</span></div>`;
            }
            const ordered = line.match(/^\s*(\d+)\.\s+(.+)$/);
            if (ordered) {
                return `<div class="ai-md-list-item"><span>${ordered[1]}.</span><span>${renderInline(ordered[2])}</span></div>`;
            }
            if (!line.trim()) return '<div class="ai-md-spacer"></div>';
            return `<div>${renderInline(line)}</div>`;
        }).join("");
    }

    function appendTyping() {
        const wrap = document.createElement("div");
        wrap.className = "ai-msg ai-msg--bot ai-typing";
        const bubble = document.createElement("div");
        bubble.className = "ai-bubble";
        bubble.innerHTML = '<span class="ai-dot"></span><span class="ai-dot"></span><span class="ai-dot"></span>';
        wrap.appendChild(bubble);
        messagesEl.appendChild(wrap);
        scrollToBottom();
        return wrap;
    }

    function loadHistory() {
        try {
            const value = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || "[]");
            return Array.isArray(value)
                ? value.filter(item => item && ["user", "assistant"].includes(item.role) && typeof item.content === "string").slice(-MAX_HISTORY_MESSAGES)
                : [];
        } catch {
            return [];
        }
    }

    function saveHistory() {
        try {
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(history));
        } catch (error) {
            console.warn("[assistant] Unable to persist chat history", error);
        }
    }

    function setLoading(loading) {
        sendBtn.disabled = loading;
        input.disabled = loading;
    }

    function scrollToBottom() {
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }
})();
