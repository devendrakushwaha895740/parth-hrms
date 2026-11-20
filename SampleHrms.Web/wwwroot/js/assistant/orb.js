(function (window, document) {
    "use strict";

    const STORAGE_KEY = "hrms-assistant-pos";
    const MOVE_THRESHOLD = 5;

    function createOrb(element, onActivate, onPositionChange) {
        let position = readPosition();
        let pointerStart = null;
        let dragged = false;
        let state = "idle";

        function viewport() {
            const visual = window.visualViewport;
            return {
                width: visual ? visual.width : document.documentElement.clientWidth,
                height: visual ? visual.height : document.documentElement.clientHeight
            };
        }

        function safeArea() {
            const styles = window.getComputedStyle(element);
            return {
                top: parseFloat(styles.getPropertyValue("--hrms-safe-top")) || 0,
                right: parseFloat(styles.getPropertyValue("--hrms-safe-right")) || 0,
                bottom: parseFloat(styles.getPropertyValue("--hrms-safe-bottom")) || 0,
                left: parseFloat(styles.getPropertyValue("--hrms-safe-left")) || 0
            };
        }

        function clamp(value) {
            const size = element.classList.contains("is-acting") ? 36 : 56;
            const view = viewport();
            const safe = safeArea();
            return {
                x: Math.min(Math.max(12 + safe.left, value.x), Math.max(12 + safe.left, view.width - size - 12 - safe.right)),
                y: Math.min(Math.max(12 + safe.top, value.y), Math.max(12 + safe.top, view.height - size - 12 - safe.bottom))
            };
        }

        function readPosition() {
            try {
                const saved = JSON.parse(window.localStorage.getItem(STORAGE_KEY) || "null");
                if (saved && Number.isFinite(saved.x) && Number.isFinite(saved.y)) {
                    return { x: saved.x, y: saved.y };
                }
            } catch (error) {
                // Storage may be unavailable in private browsing; keep the in-memory position.
            }
            const view = viewport();
            return { x: view.width - 80, y: view.height - 96 };
        }

        function save() {
            try {
                window.localStorage.setItem(STORAGE_KEY, JSON.stringify(position));
            } catch (error) {
                // The assistant remains usable when storage is disabled.
            }
        }

        function render() {
            position = clamp(position);
            element.style.left = position.x + "px";
            element.style.top = position.y + "px";
            if (onPositionChange) onPositionChange(position);
        }

        function finishPointer(event) {
            if (!pointerStart || event.pointerId !== pointerStart.id) return;
            if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId);
            if (dragged) {
                const view = viewport();
                const size = element.classList.contains("is-acting") ? 36 : 56;
                const safe = safeArea();
                position.x = pointerStart.currentX < view.width / 2
                    ? 12 + safe.left
                    : view.width - size - 12 - safe.right;
                render();
                save();
            } else if (event.type === "pointerup") {
                onActivate();
            }
            pointerStart = null;
            dragged = false;
        }

        element.addEventListener("pointerdown", function (event) {
            if (event.button !== undefined && event.button !== 0) return;
            pointerStart = {
                id: event.pointerId,
                x: event.clientX,
                y: event.clientY,
                originX: position.x,
                originY: position.y,
                currentX: event.clientX
            };
            dragged = false;
            element.setPointerCapture(event.pointerId);
        });

        element.addEventListener("pointermove", function (event) {
            if (!pointerStart || event.pointerId !== pointerStart.id) return;
            pointerStart.currentX = event.clientX;
            const dx = event.clientX - pointerStart.x;
            const dy = event.clientY - pointerStart.y;
            if (!dragged && Math.hypot(dx, dy) >= MOVE_THRESHOLD) dragged = true;
            if (dragged) {
                position = { x: pointerStart.originX + dx, y: pointerStart.originY + dy };
                render();
            }
        });

        element.addEventListener("pointerup", finishPointer);
        element.addEventListener("pointercancel", finishPointer);
        element.addEventListener("click", function (event) {
            if (event.detail === 0) onActivate();
        });
        window.addEventListener("resize", function () {
            render();
            save();
        });
        if (window.visualViewport) {
            window.visualViewport.addEventListener("resize", function () {
                render();
                save();
            });
        }

        function setState(nextState) {
            const states = ["idle", "listening", "thinking", "speaking", "acting"];
            state = states.indexOf(nextState) >= 0 ? nextState : "idle";
            element.classList.remove("is-idle", "is-listening", "is-thinking", "is-speaking", "is-acting");
            element.classList.add("is-" + state);
            if (state === "acting") {
                const view = viewport();
                const safe = safeArea();
                position.x = position.x + 28 < view.width / 2
                    ? 12 + safe.left
                    : view.width - 36 - 12 - safe.right;
            }
            render();
            if (state === "acting") save();
        }

        render();
        return {
            setState: setState,
            getPosition: function () { return { x: position.x, y: position.y }; },
            getElement: function () { return element; }
        };
    }

    window.HrmsAssistantOrb = { create: createOrb };
})(window, document);
