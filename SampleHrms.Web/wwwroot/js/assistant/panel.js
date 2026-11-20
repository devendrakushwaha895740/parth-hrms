(function (window, document) {
    "use strict";

    function createPanel(elements, orbElement) {
        const panel = elements.panel;
        const messages = elements.messages;
        let typingIndicator = null;

        function positionPanel() {
            if (!panel.classList.contains("is-open")) return;
            const width = window.innerWidth;
            const height = window.innerHeight;
            const orbRect = orbElement.getBoundingClientRect();
            if (width <= 600) {
                panel.classList.add("is-mobile");
                panel.style.left = "0px";
                panel.style.top = "auto";
                panel.style.bottom = "0px";
                return;
            }

            panel.classList.remove("is-mobile");
            panel.style.bottom = "auto";
            const panelWidth = Math.min(360, width - 32);
            panel.style.width = panelWidth + "px";
            const leftSide = orbRect.left + orbRect.width / 2 < width / 2;
            let left = leftSide ? orbRect.right + 12 : orbRect.left - panelWidth - 12;
            let top = orbRect.top + orbRect.height / 2 - 220;
            if (left + panelWidth > width - 12) left = width - panelWidth - 12;
            if (left < 12) left = 12;
            top = Math.max(12, Math.min(top, height - 12 - Math.min(panel.offsetHeight || height * 0.7, height - 24)));
            panel.style.left = left + "px";
            panel.style.top = top + "px";
        }

        function positionPill() {
            const pill = elements.pill;
            if (!pill.classList.contains("is-visible")) return;
            const viewWidth = window.innerWidth;
            const viewHeight = window.innerHeight;
            const orbRect = orbElement.getBoundingClientRect();
            const pillWidth = Math.min(pill.offsetWidth || 220, viewWidth - 24);
            const pillHeight = pill.offsetHeight || 42;
            let left = orbRect.left < viewWidth / 2
                ? orbRect.right + 8
                : orbRect.left - pillWidth - 8;
            let top = orbRect.top - pillHeight - 10;
            left = Math.max(12, Math.min(left, viewWidth - pillWidth - 12));
            if (top < 12) top = Math.min(viewHeight - pillHeight - 12, orbRect.bottom + 10);
            pill.style.left = left + "px";
            pill.style.top = Math.max(12, top) + "px";
        }

        function updatePill(visible, text) {
            const pill = elements.pill;
            pill.classList.toggle("is-visible", visible);
            pill.setAttribute("aria-hidden", visible ? "false" : "true");
            pill.querySelector(".hrms-assistant-pill-text").textContent = text || "Voice mode on";
            positionPill();
        }

        function positionElements() {
            positionPanel();
            positionPill();
        }

        function open() {
            panel.classList.add("is-open");
            panel.setAttribute("aria-hidden", "false");
            positionPanel();
            elements.input.focus();
        }

        function close() {
            panel.classList.remove("is-open");
            panel.setAttribute("aria-hidden", "true");
        }

        function addMessage(role, text) {
            const bubble = document.createElement("div");
            bubble.className = "hrms-assistant-message hrms-assistant-message-" + (role === "user" ? "user" : "assistant");
            bubble.textContent = text;
            bubble.dataset.role = role;
            messages.appendChild(bubble);
            scrollToBottom();
        }

        function addStep(text, isSlowNotice) {
            const step = document.createElement("div");
            step.className = "hrms-assistant-step" + (isSlowNotice ? " hrms-assistant-step-slow-notice" : "");
            step.textContent = text;
            messages.appendChild(step);
            scrollToBottom();
        }

        function clearSlowNotice() {
            messages.querySelectorAll(".hrms-assistant-step-slow-notice").forEach(function (step) {
                step.remove();
            });
        }

        function addConfirmation(summary, actionId, onChoice) {
            const card = document.createElement("div");
            card.className = "hrms-assistant-confirmation";
            card.dataset.actionId = actionId;
            const text = document.createElement("div");
            text.className = "hrms-assistant-confirmation-text";
            text.textContent = summary;
            const actions = document.createElement("div");
            actions.className = "hrms-assistant-confirmation-actions";
            const yes = document.createElement("button");
            yes.type = "button";
            yes.textContent = "Yes";
            yes.setAttribute("data-agent-ignore", "true");
            yes.className = "hrms-assistant-confirm-yes";
            const no = document.createElement("button");
            no.type = "button";
            no.textContent = "No";
            no.setAttribute("data-agent-ignore", "true");
            no.className = "hrms-assistant-confirm-no";
            function choose(approved) {
                yes.disabled = true;
                no.disabled = true;
                onChoice(approved);
            }
            yes.addEventListener("click", function () { choose(true); });
            no.addEventListener("click", function () { choose(false); });
            actions.append(yes, no);
            card.append(text, actions);
            messages.appendChild(card);
            scrollToBottom();
        }

        function showTyping() {
            if (typingIndicator) return;
            typingIndicator = document.createElement("div");
            typingIndicator.className = "hrms-assistant-typing";
            typingIndicator.setAttribute("aria-label", "Assistant is typing");
            typingIndicator.innerHTML = "<span></span><span></span><span></span>";
            messages.appendChild(typingIndicator);
            scrollToBottom();
        }

        function hideTyping() {
            if (typingIndicator) typingIndicator.remove();
            typingIndicator = null;
        }

        function clear() {
            messages.replaceChildren();
            typingIndicator = null;
        }

        function scrollToBottom() {
            messages.scrollTop = messages.scrollHeight;
        }

        elements.close.addEventListener("click", elements.onClose);
        elements.minimize.addEventListener("click", elements.onMinimize);
        elements.form.addEventListener("submit", function (event) {
            event.preventDefault();
            const text = elements.input.value.trim();
            if (!text) return;
            elements.input.value = "";
            elements.onSend(text, "text");
        });
        window.addEventListener("resize", function () {
            positionPanel();
            positionPill();
        });

        return {
            open: open,
            close: close,
            addMessage: addMessage,
            addStep: addStep,
            clearSlowNotice: clearSlowNotice,
            addConfirmation: addConfirmation,
            clear: clear,
            showTyping: showTyping,
            hideTyping: hideTyping,
            position: positionElements,
            updatePill: updatePill
        };
    }

    window.HrmsAssistantPanel = { create: createPanel };
})(window, document);
