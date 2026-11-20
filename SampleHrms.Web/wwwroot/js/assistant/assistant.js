(function (window, document) {
    "use strict";

    const root = document.getElementById("ai-assistant-root");
    if (!root || !window.HrmsAssistantOrb || !window.HrmsAssistantPanel) return;

    const HISTORY_KEY = "hrms-parth-chat-v1";
    const PANEL_KEY = "hrms-parth-panel-open";
    const history = readHistory();
    let userMessageCallback = null;
    let pendingConfirmation = null;
    let storageWarningShown = false;
    let voice = null;

    root.innerHTML = [
        '<button type="button" class="hrms-assistant-orb is-idle" aria-label="AI assistant" aria-haspopup="dialog" aria-expanded="false" data-agent-ignore="true">',
        '  <span class="hrms-assistant-orb-icon" aria-hidden="true">✦</span>',
        '</button>',
        '<section class="hrms-assistant-panel" role="dialog" aria-label="Parth" aria-hidden="true" data-agent-ignore="true">',
        '  <header class="hrms-assistant-header">',
        '    <div><span class="hrms-assistant-header-mark" aria-hidden="true">✦</span><strong>Parth</strong></div>',
        '    <div class="hrms-assistant-header-actions">',
        '      <button type="button" class="hrms-assistant-icon-button hrms-assistant-minimize" aria-label="Minimize assistant" data-agent-ignore="true">−</button>',
        '      <button type="button" class="hrms-assistant-icon-button hrms-assistant-close" aria-label="Close assistant" data-agent-ignore="true">×</button>',
        '    </div>',
        '  </header>',
        '  <div class="hrms-assistant-voice-bar">',
        '    <canvas class="hrms-assistant-waveform" aria-hidden="true"></canvas>',
        '    <label class="hrms-assistant-language-label" for="hrms-assistant-language" data-agent-ignore="true">Language</label>',
        '    <select id="hrms-assistant-language" class="hrms-assistant-language" aria-label="Voice language" data-agent-ignore="true">',
        '      <option value="hi-IN">हिंदी / Hinglish</option>',
        '      <option value="en-IN">English (India)</option>',
        '    </select>',
        '    <span class="hrms-assistant-voice-status" role="status" aria-live="polite"></span>',
        '  </div>',
        '  <div class="hrms-assistant-voice-settings">',
        '    <label for="hrms-assistant-interaction-mode" data-agent-ignore="true">Mode</label>',
        '    <select id="hrms-assistant-interaction-mode" class="hrms-assistant-interaction-mode" aria-label="Voice interaction mode" data-agent-ignore="true">',
        '      <option value="hands-free">Hands-free</option>',
        '      <option value="push-to-talk">Hold to talk</option>',
        '    </select>',
        '    <label class="hrms-assistant-interrupt-setting" data-agent-ignore="true">',
        '      <input type="checkbox" class="hrms-assistant-allow-interrupt" data-agent-ignore="true">',
        '      Allow interrupt by voice (use with headphones)',
        '    </label>',
        '  </div>',
        '  <div class="hrms-assistant-messages" aria-live="polite"></div>',
        '  <form class="hrms-assistant-compose" data-agent-ignore="true">',
        '    <textarea rows="1" aria-label="Message" placeholder="Ask Parth..." data-agent-ignore="true"></textarea>',
        '    <div class="hrms-assistant-compose-actions">',
        '      <button type="button" class="hrms-assistant-mic" aria-label="Start voice input" data-agent-ignore="true">Mic</button>',
        '      <button type="button" class="hrms-assistant-voice-mode" aria-pressed="false" data-agent-ignore="true">◉ Voice mode</button>',
        '      <button type="button" class="hrms-assistant-mute" aria-pressed="false" data-agent-ignore="true">Mute</button>',
        '      <button type="submit" class="hrms-assistant-send" aria-label="Send message" data-agent-ignore="true">Send</button>',
        '    </div>',
        '  </form>',
        '</section>',
        '<button type="button" class="hrms-assistant-voice-pill" aria-label="Tap to continue listening" aria-hidden="true" data-agent-ignore="true">',
        '  <canvas class="hrms-assistant-pill-waveform" aria-hidden="true"></canvas>',
        '  <span class="hrms-assistant-pill-text">Voice mode on</span>',
        '</button>'
    ].join("");

    const orbElement = root.querySelector(".hrms-assistant-orb");
    const panelElement = root.querySelector(".hrms-assistant-panel");
    const elements = {
        panel: panelElement,
        messages: root.querySelector(".hrms-assistant-messages"),
        input: root.querySelector("textarea"),
        form: root.querySelector(".hrms-assistant-compose"),
        close: root.querySelector(".hrms-assistant-close"),
        minimize: root.querySelector(".hrms-assistant-minimize"),
        waveform: root.querySelector(".hrms-assistant-waveform"),
        pill: root.querySelector(".hrms-assistant-voice-pill"),
        pillWaveform: root.querySelector(".hrms-assistant-pill-waveform"),
        language: root.querySelector(".hrms-assistant-language"),
        interactionMode: root.querySelector(".hrms-assistant-interaction-mode"),
        allowVoiceInterrupt: root.querySelector(".hrms-assistant-allow-interrupt"),
        voiceMode: root.querySelector(".hrms-assistant-voice-mode"),
        mute: root.querySelector(".hrms-assistant-mute"),
        mic: root.querySelector(".hrms-assistant-mic"),
        status: root.querySelector(".hrms-assistant-voice-status"),
        orb: orbElement,
        onSend: handleUserMessage,
        onClose: close,
        onMinimize: minimize
    };

    const panel = window.HrmsAssistantPanel.create(elements, orbElement);
    const orb = window.HrmsAssistantOrb.create(orbElement, open, panel.position);
    restoreHistory();
    voice = window.HrmsAssistantVoice.create({
        panel: panelElement,
        input: elements.input,
        waveform: elements.waveform,
        pill: elements.pill,
        pillWaveform: elements.pillWaveform,
        language: elements.language,
        interactionMode: elements.interactionMode,
        allowVoiceInterrupt: elements.allowVoiceInterrupt,
        voiceMode: elements.voiceMode,
        mute: elements.mute,
        mic: elements.mic,
        status: elements.status,
        orb: orbElement
    }, {
        addMessage: addMessage,
        sendText: handleUserMessage,
        openPanel: open,
        setVisualState: setVisualState,
        updatePill: panel.updatePill
    });

    function open() {
        if (voice && voice.isSpeaking()) voice.interrupt();
        panel.open();
        orbElement.setAttribute("aria-expanded", "true");
        setSessionValue(PANEL_KEY, "true");
        if (voice) voice.refreshPill();
    }

    function close() {
        if (voice) voice.setVoiceMode(false);
        panel.close();
        orbElement.setAttribute("aria-expanded", "false");
        setSessionValue(PANEL_KEY, "false");
        orbElement.focus();
    }

    function minimize() {
        panel.close();
        orbElement.setAttribute("aria-expanded", "false");
        setSessionValue(PANEL_KEY, "false");
        if (voice) voice.refreshPill();
    }

    function setVisualState(state) {
        orb.setState(state);
        panelElement.classList.toggle("is-speaking", state === "speaking");
    }

    function setAssistantState(state) {
        setVisualState(state);
        if (voice) voice.setAgentState(state);
    }

    function handleUserMessage(text, source) {
        if (voice && !voice.onUserSubmitted(text, source || "text")) return;
        addMessage("user", text);
        if (pendingConfirmation) {
            const response = text.trim().toLocaleLowerCase().replace(/[.!?।,]+$/g, "").replace(/\s+/g, " ");
            const approved = /^(haan|han|ha|yes|y|ok|okay|sure|kar do|kardo|haan kar do|han kar do)$/.test(response);
            const declined = /^(no|nahi|nahin|na|n|ruko|ruk jao|mat karo)$/.test(response);
            if (approved || declined) {
                const confirmation = pendingConfirmation;
                pendingConfirmation = null;
                confirmation.callback(approved);
            } else {
                addMessage("assistant", "Please reply yes/haan to confirm or no/nahi to cancel.");
            }
            return;
        }

        if (userMessageCallback) {
            Promise.resolve().then(function () { return userMessageCallback(text); }).catch(function (error) {
                addMessage("assistant", error && error.message ? error.message : "Parth could not connect. Please try again.");
            });
            return;
        }

        setAssistantState("thinking");
        panel.showTyping();
        window.setTimeout(function () {
            panel.hideTyping();
            addMessage("assistant", "AI is not configured yet");
            setAssistantState("idle");
        }, 500);
    }

    function addMessage(role, text) {
        const content = role === "assistant" ? cleanAssistantText(text) : String(text || "");
        if (role === "assistant") clearSlowNotice();
        panel.addMessage(role, content);
        saveHistory({ kind: "message", role: role, text: content });
        if (role === "assistant" && voice) voice.speak(content);
    }

    function addStep(text, speakInVoice, isSlowNotice) {
        const content = String(text || "");
        panel.addStep(content, Boolean(isSlowNotice));
        saveHistory({ kind: isSlowNotice ? "slow-notice" : "step", text: content });
        if (speakInVoice && voice) voice.speakNotice(content);
    }

    function clearSlowNotice() {
        panel.clearSlowNotice();
        for (let index = history.length - 1; index >= 0; index -= 1) {
            if (history[index].kind === "slow-notice") history.splice(index, 1);
        }
        try { window.sessionStorage.setItem(HISTORY_KEY, JSON.stringify(history)); } catch (error) { warnStorage(); }
    }

    function addConfirmation(summary, actionId, callback) {
        if (pendingConfirmation && pendingConfirmation.actionId === actionId) return;
        pendingConfirmation = { actionId: actionId, callback: callback };
        const cleanSummary = cleanAssistantText(summary);
        panel.addConfirmation(cleanSummary, actionId, function (approved) {
            pendingConfirmation = null;
            callback(approved);
        });
        if (voice) voice.speak(cleanSummary);
    }

    function cleanAssistantText(text) {
        return String(text || "")
            .replace(/```[\s\S]*?```/g, " ")
            .replace(/!\[([^\]]*)\]\([^)]+\)/g, "$1")
            .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
            .replace(/`([^`]*)`/g, "$1")
            .replace(/(^|\n)\s{0,3}#{1,6}\s*/g, "$1")
            .replace(/(^|\n)\s*[-*+]\s+/g, "$1")
            .replace(/(^|\n)\s*\d+[.)]\s+/g, "$1")
            .replace(/(\*\*|__)(.*?)\1/g, "$2")
            .replace(/(\*|_)([^*_]+)\1/g, "$2")
            .replace(/~~(.*?)~~/g, "$1")
            .replace(/[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}]/gu, "")
            .replace(/[ \t]+\n/g, "\n")
            .replace(/\n{3,}/g, "\n\n")
            .trim();
    }

    function clear() {
        panel.clear();
        history.length = 0;
        pendingConfirmation = null;
        try { window.sessionStorage.removeItem(HISTORY_KEY); } catch (error) { warnStorage(); }
    }

    function readHistory() {
        try {
            const stored = JSON.parse(window.sessionStorage.getItem(HISTORY_KEY) || "[]");
            if (Array.isArray(stored)) return stored.filter(function (item) {
                return item && (item.kind === "message" || item.kind === "step" || item.kind === "slow-notice") &&
                    typeof item.text === "string";
            }).slice(-100);
        } catch (error) {
            warnStorage();
            return [];
        }
        return [];
    }

    function restoreHistory() {
        history.forEach(function (item) {
            if (item.kind === "step" || item.kind === "slow-notice")
                panel.addStep(item.text, item.kind === "slow-notice");
            else panel.addMessage(item.role, item.text);
        });
    }

    function saveHistory(item) {
        history.push(item);
        if (history.length > 100) history.splice(0, history.length - 100);
        try { window.sessionStorage.setItem(HISTORY_KEY, JSON.stringify(history)); } catch (error) { warnStorage(); }
    }

    function setSessionValue(key, value) {
        try { window.sessionStorage.setItem(key, value); } catch (error) { warnStorage(); }
    }

    function getSessionValue(key) {
        try { return window.sessionStorage.getItem(key); } catch (error) { warnStorage(); return null; }
    }

    function warnStorage() {
        if (storageWarningShown) return;
        storageWarningShown = true;
        console.warn("Parth browser storage is unavailable; chat history may not survive page navigation.");
    }

    const api = {
        open: open,
        close: close,
        setState: setAssistantState,
        addMessage: addMessage,
        addStep: addStep,
        addConfirmation: addConfirmation,
        showTyping: panel.showTyping,
        hideTyping: panel.hideTyping,
        startListening: function () { return voice.startListening(); },
        stopListening: function () { voice.stopListening(); },
        speak: function (text) { voice.speak(text); },
        setVoiceMode: function (enabled) { voice.setVoiceMode(enabled); },
        setLanguage: function (code) { voice.setLanguage(code); },
        clear: clear,
        onUserMessage: function (callback) {
            userMessageCallback = typeof callback === "function" ? callback : null;
        }
    };

    panelElement.addEventListener("click", function (event) {
        if (event.target.closest("[data-agent-ignore='true']")) event.stopPropagation();
    });

    window.HrmsAssistant = api;
    if (getSessionValue(PANEL_KEY) === "true")
        window.setTimeout(open, 0);
})(window, document);
