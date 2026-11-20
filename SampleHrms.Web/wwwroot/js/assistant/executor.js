(function (window, document) {
    "use strict";

    if (!window.HrmsAssistant || !window.signalR) return;

    const STATE_KEY = "hrms-parth-session";
    const PANEL_KEY = "hrms-parth-panel-open";
    const HISTORY_KEY = "hrms-parth-chat-v1";
    const navigationIds = {
        dashboard: "nav-dashboard",
        "leave-apply": "nav-leave-apply",
        "leave-history": "nav-leave-history",
        "attendance-my": "nav-attendance-my",
        employees: "nav-employees",
        "leave-pending": "nav-leave-pending",
        "leave-all": "nav-leave-all",
        "attendance-manage": "nav-attendance-manage",
        "leave-types": "nav-leave-types"
    };

    const state = readState();
    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl("/agentHub")
        .withAutomaticReconnect()
        .build();
    let cursor = null;
    let connectPromise = null;
    let storageWarningShown = false;

    connection.on("AssistantText", function (text) {
        window.HrmsAssistant.hideTyping();
        window.HrmsAssistant.addMessage("assistant", text);
    });
    connection.on("StepLog", function (text, speakInVoice, isSlowNotice) {
        window.HrmsAssistant.addStep(text, speakInVoice, isSlowNotice);
    });
    connection.on("StateChanged", function (nextState) {
        window.HrmsAssistant.setState(nextState);
        if (nextState === "thinking") window.HrmsAssistant.showTyping();
        else if (nextState !== "acting") window.HrmsAssistant.hideTyping();
    });
    connection.on("Error", function (message) {
        window.HrmsAssistant.hideTyping();
        window.HrmsAssistant.addMessage("assistant", message);
        if (String(message).indexOf("session expired") >= 0) {
            state.pending = null;
            state.active = false;
            saveState();
        }
    });
    connection.on("ConfirmRequest", function (actionId, summary) {
        window.HrmsAssistant.hideTyping();
        window.HrmsAssistant.addConfirmation(summary, actionId, function (approved) {
            connection.invoke("ConfirmAction", state.sessionId, actionId, approved)
                .catch(showConnectionError);
        });
    });
    connection.on("ToolRequest", function (toolCallId, name, args) {
        if (state.pending && state.pending.toolCallId === toolCallId) return;
        executeTool(toolCallId, name, args || {});
    });
    connection.onreconnected(function () {
        resumeAndRecover(false, true).catch(showConnectionError);
    });
    connection.onclose(function () {
        window.setTimeout(function () { startConnection(false); }, 3000);
    });

    window.HrmsAssistant.onUserMessage(sendMessage);
    const logout = findAgentElement("nav-logout");
    if (logout) logout.addEventListener("click", clearSessionForLogout);

    startConnection(true);

    async function startConnection(firstLoad) {
        if (connectPromise) return connectPromise;
        connectPromise = (async function () {
            try {
                if (connection.state === window.signalR.HubConnectionState.Disconnected)
                    await connection.start();
                if (state.active)
                    await resumeAndRecover(firstLoad, firstLoad || Boolean(state.pending && state.pending.stage === "result-ready"));
            } catch (error) {
                showConnectionError(error);
                window.setTimeout(function () { startConnection(false); }, 4000);
            } finally {
                connectPromise = null;
            }
        })();
        return connectPromise;
    }

    async function ensureConnected() {
        if (connection.state === window.signalR.HubConnectionState.Connected) return;
        await startConnection(false);
        if (connection.state !== window.signalR.HubConnectionState.Connected)
            throw new Error("Parth could not connect to the HRMS assistant service.");
    }

    async function resumeAndRecover(isPageLoad, recoverPending) {
        if (connection.state !== window.signalR.HubConnectionState.Connected || !state.active) return;
        await connection.invoke("Resume", state.sessionId);
        if (recoverPending && state.pending && (isPageLoad || state.pending.stage === "result-ready")) {
            let result = state.pending.result;
            if (state.pending.stage === "awaiting-navigation")
                result = pageReadyResult(state.pending.name);
            else if (state.pending.stage !== "result-ready")
                result = JSON.stringify({ success: false, error: "The page reloaded before this UI action finished." });
            await sendToolResult(state.pending.toolCallId, result);
        }
    }

    async function sendMessage(text) {
        await ensureConnected();
        state.active = true;
        saveState();
        await connection.invoke("SendMessage", state.sessionId, text);
    }

    async function executeTool(toolCallId, name, args) {
        const pending = { toolCallId: toolCallId, name: name, stage: "executing", result: null };
        state.pending = pending;
        saveState();
        try {
            window.HrmsAssistant.setState("acting");
            let result;
            switch (name) {
                case "ui_navigate":
                    result = await navigate(args.pageKey, pending);
                    break;
                case "ui_read_page":
                    result = readPage();
                    break;
                case "ui_fill":
                    result = await fillField(args.agentId, args.value);
                    break;
                case "ui_select":
                    result = await selectOption(args.agentId, args.value);
                    break;
                case "ui_click":
                    result = await clickControl(args.agentId, pending);
                    break;
                default:
                    throw new Error("Unsupported browser tool.");
            }

            if (result && result.deferred) return;
            await sendToolResult(toolCallId, result);
        } catch (error) {
            await sendToolResult(toolCallId, JSON.stringify({
                success: false,
                error: error && error.message ? error.message : "The UI action could not be completed."
            }));
        }
    }

    async function navigate(pageKey, pending) {
        const agentId = navigationIds[pageKey];
        if (!agentId) throw new Error("Unknown HRMS page.");
        let target = findAgentElement(agentId);
        if (!target) {
            const toggle = findAgentElement("nav-toggle");
            if (toggle) {
                await humanizeTarget(toggle);
                toggle.click();
                await delay(350);
                target = findAgentElement(agentId);
            }
        }
        if (!target || target.tagName !== "A") throw new Error("That page is not available in the current role.");
        return clickWithNavigation(target, pending, false);
    }

    async function clickControl(agentId, pending) {
        const target = findAgentElement(agentId);
        if (!target || target.disabled) throw new Error("That control is not visible or is disabled.");
        if (!/^(A|BUTTON|INPUT)$/.test(target.tagName))
            throw new Error("Only visible links and buttons can be clicked.");
        return clickWithNavigation(target, pending, true);
    }

    async function clickWithNavigation(target, pending, allowFormSubmit) {
        await humanizeTarget(target);
        const link = target.closest("a[href]");
        const form = allowFormSubmit && isSubmitControl(target) ? target.closest("form") : null;
        if (link && !isSameOrigin(link.href)) throw new Error("Navigation outside this HRMS app is not allowed.");

        if (form) addAgentSource(form);
        const causesNavigation = Boolean(link || form);
        if (causesNavigation) {
            pending.stage = "awaiting-navigation";
            saveState();
        }
        const pageChanging = waitForPageHide(causesNavigation ? 1800 : 0);
        target.click();
        if (causesNavigation && await pageChanging) return { deferred: true };
        if (causesNavigation) {
            pending.stage = "executing";
            saveState();
        }
        return JSON.stringify({
            success: true,
            clicked: target.getAttribute("data-agent-id"),
            page: document.body.getAttribute("data-agent-page"),
            feedback: readFlash()
        });
    }

    function isSubmitControl(target) {
        return (target.tagName === "BUTTON" && target.type === "submit") ||
            (target.tagName === "INPUT" && (target.type === "submit" || target.type === "image"));
    }

    function addAgentSource(form) {
        let source = Array.from(form.elements).find(function (element) { return element.name === "agent_source"; });
        if (!source) {
            source = document.createElement("input");
            source.type = "hidden";
            source.name = "agent_source";
            form.appendChild(source);
        }
        source.value = "AI";
    }

    async function fillField(agentId, value) {
        const target = findAgentElement(agentId);
        if (!target || target.disabled || target.readOnly) throw new Error("That field is not visible or cannot be edited.");
        if (target.tagName !== "INPUT" && target.tagName !== "TEXTAREA")
            throw new Error("The requested control is not a text input.");
        const text = String(value == null ? "" : value);
        if (text.length > 2000) throw new Error("Input is too long.");
        await humanizeTarget(target);
        target.focus();
        if (target.type === "date" || target.type === "time" || target.type === "number") {
            await delay(Math.min(text.length * 30, 1200));
            setNativeValue(target, text);
        } else {
            setNativeValue(target, "");
            for (const character of text) {
                setNativeValue(target, target.value + character);
                await delay(30);
            }
        }
        target.dispatchEvent(new Event("change", { bubbles: true }));
        return JSON.stringify({ success: true, agentId: agentId, filled: true });
    }

    async function selectOption(agentId, value) {
        const target = findAgentElement(agentId);
        if (!target || target.tagName !== "SELECT" || target.disabled)
            throw new Error("That dropdown is not visible or enabled.");
        const wanted = String(value == null ? "" : value);
        const option = Array.from(target.options).find(function (item) {
            return item.value === wanted || item.text.trim().toLocaleLowerCase() === wanted.trim().toLocaleLowerCase();
        });
        if (!option) throw new Error("That option is not available in the dropdown.");
        await humanizeTarget(target);
        target.classList.add("hrms-agent-selecting");
        await delay(180);
        target.value = option.value;
        target.dispatchEvent(new Event("input", { bubbles: true }));
        target.dispatchEvent(new Event("change", { bubbles: true }));
        target.classList.remove("hrms-agent-selecting");
        return JSON.stringify({ success: true, agentId: agentId, selected: option.text });
    }

    function readPage() {
        const elements = [];
        document.querySelectorAll("[data-agent-id]").forEach(function (element) {
            if (elements.length >= 120 || !isVisible(element) || isIgnored(element)) return;
            const id = element.getAttribute("data-agent-id");
            const item = {
                id: id,
                tag: element.tagName.toLowerCase(),
                label: getLabel(element).slice(0, 180),
                disabled: Boolean(element.disabled)
            };
            if (element.tagName === "SELECT") {
                item.value = element.value;
                item.options = Array.from(element.options).slice(0, 40).map(function (option) {
                    return { value: option.value, label: option.text.trim() };
                });
            } else if (element.tagName === "INPUT" || element.tagName === "TEXTAREA") {
                item.value = element.type === "password" ? "[redacted]" : String(element.value || "").slice(0, 300);
            }
            elements.push(item);
        });
        return JSON.stringify({
            page: document.body.getAttribute("data-agent-page") || "",
            elements: elements,
            feedback: readFlash()
        });
    }

    function findAgentElement(agentId) {
        if (typeof agentId !== "string" || !agentId) return null;
        const candidates = document.querySelectorAll("[data-agent-id]");
        for (const element of candidates) {
            if (element.getAttribute("data-agent-id") === agentId && isVisible(element) && !isIgnored(element))
                return element;
        }
        return null;
    }

    function isIgnored(element) {
        return element.hasAttribute("data-agent-ignore") || Boolean(element.closest('[data-agent-ignore="true"]'));
    }

    function isVisible(element) {
        if (!element.getClientRects().length) return false;
        const style = window.getComputedStyle(element);
        return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) !== 0;
    }

    function getLabel(element) {
        const aria = element.getAttribute("aria-label");
        if (aria) return aria;
        if (element.labels && element.labels.length)
            return Array.from(element.labels).map(function (label) { return label.textContent.trim(); }).join(" ");
        return (element.innerText || element.textContent || element.getAttribute("placeholder") || "").trim();
    }

    function readFlash() {
        const success = findAgentElement("flash-success");
        const error = findAgentElement("flash-error");
        return {
            success: success ? success.innerText.trim() : "",
            error: error ? error.innerText.trim() : ""
        };
    }

    function pageReadyResult(toolName) {
        const feedback = readFlash();
        return JSON.stringify({
            success: true,
            action: toolName,
            page: window.location.pathname,
            pageKey: document.body.getAttribute("data-agent-page"),
            ready: true,
            feedback: feedback
        });
    }

    async function humanizeTarget(target) {
        window.HrmsAssistant.setState("acting");
        target.scrollIntoView({ behavior: "smooth", block: "center", inline: "nearest" });
        await delay(220);
        const rect = target.getBoundingClientRect();
        const x = rect.left + rect.width / 2;
        const y = rect.top + rect.height / 2;
        if (!cursor) {
            cursor = document.createElement("div");
            cursor.className = "hrms-agent-cursor";
            cursor.setAttribute("data-agent-ignore", "true");
            document.body.appendChild(cursor);
        }
        const startX = parseFloat(cursor.style.left) || Math.max(0, x - 100);
        const startY = parseFloat(cursor.style.top) || Math.max(0, y - 70);
        cursor.style.opacity = "1";
        const animation = cursor.animate([
            { left: startX + "px", top: startY + "px" },
            { left: x + "px", top: y + "px" }
        ], { duration: 400, easing: "ease-out", fill: "forwards" });
        await animation.finished;
        cursor.style.left = x + "px";
        cursor.style.top = y + "px";
        target.classList.add("hrms-agent-highlight");
        target.addEventListener("animationend", function () {
            target.classList.remove("hrms-agent-highlight");
        }, { once: true });
        rippleAt(x, y);
    }

    function rippleAt(x, y) {
        const ripple = document.createElement("span");
        ripple.className = "hrms-agent-ripple";
        ripple.setAttribute("data-agent-ignore", "true");
        ripple.style.left = x + "px";
        ripple.style.top = y + "px";
        document.body.appendChild(ripple);
        window.setTimeout(function () { ripple.remove(); }, 650);
    }

    function setNativeValue(element, value) {
        const prototype = element.tagName === "TEXTAREA" ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(prototype, "value").set;
        setter.call(element, value);
        element.dispatchEvent(new Event("input", { bubbles: true }));
    }

    function waitForPageHide(timeoutMs) {
        if (!timeoutMs) return Promise.resolve(false);
        return new Promise(function (resolve) {
            let finished = false;
            const finish = function (hidden) {
                if (finished) return;
                finished = true;
                window.removeEventListener("pagehide", onHide);
                resolve(hidden);
            };
            const onHide = function () { finish(true); };
            window.addEventListener("pagehide", onHide, { once: true });
            window.setTimeout(function () { finish(false); }, timeoutMs);
        });
    }

    async function sendToolResult(toolCallId, result) {
        const current = state.pending;
        if (!current || current.toolCallId !== toolCallId) return;
        current.stage = "result-ready";
        current.result = typeof result === "string" ? result : JSON.stringify(result);
        saveState();
        await ensureConnected();
        await connection.invoke("SubmitToolResult", state.sessionId, toolCallId, current.result);
        if (state.pending === current) {
            state.pending = null;
            saveState();
        }
        window.HrmsAssistant.setState("thinking");
    }

    function readState() {
        try {
            const saved = JSON.parse(window.sessionStorage.getItem(STATE_KEY) || "null");
            if (saved && typeof saved.sessionId === "string")
                return { sessionId: saved.sessionId, active: Boolean(saved.active), pending: saved.pending || null };
        } catch (error) {
            warnStorage("Parth could not restore its browser session; a new session will be started.");
        }
        return { sessionId: newSessionId(), active: false, pending: null };
    }

    function saveState() {
        try {
            window.sessionStorage.setItem(STATE_KEY, JSON.stringify(state));
        } catch (error) {
            warnStorage("Parth session storage is unavailable; page navigation resume may not work.");
        }
    }

    function clearSessionForLogout() {
        window.HrmsAssistant.setVoiceMode(false);
        try {
            window.sessionStorage.removeItem(STATE_KEY);
            window.sessionStorage.removeItem(PANEL_KEY);
            window.sessionStorage.removeItem(HISTORY_KEY);
            window.sessionStorage.removeItem("hrms-parth-voice-mode");
            window.sessionStorage.removeItem("hrms-parth-agent-busy");
        } catch (error) {
            warnStorage("Parth could not clear its browser session during logout.");
        }
    }

    function warnStorage(message) {
        if (storageWarningShown) return;
        storageWarningShown = true;
        console.warn(message);
    }

    function newSessionId() {
        if (window.crypto && typeof window.crypto.randomUUID === "function") return window.crypto.randomUUID();
        return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (character) {
            const random = Math.random() * 16 | 0;
            return (character === "x" ? random : (random & 3 | 8)).toString(16);
        });
    }

    function isSameOrigin(url) {
        try { return new URL(url, window.location.href).origin === window.location.origin; }
        catch (error) { return false; }
    }

    function delay(ms) { return new Promise(function (resolve) { window.setTimeout(resolve, ms); }); }

    function showConnectionError(error) {
        window.HrmsAssistant.hideTyping();
        window.HrmsAssistant.setState("idle");
        window.HrmsAssistant.addMessage("assistant",
            error && error.message ? error.message : "Parth could not connect. Please try again.");
    }
})(window, document);
