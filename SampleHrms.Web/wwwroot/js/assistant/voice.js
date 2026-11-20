(function (window, document) {
    "use strict";

    const LANGUAGE_KEY = "hrms-parth-voice-language";
    const MODE_KEY = "hrms-parth-voice-mode";
    const BUSY_KEY = "hrms-parth-agent-busy";
    const INTERACTION_KEY = "hrms-parth-interaction-mode";
    const INTERRUPT_KEY = "hrms-parth-allow-voice-interrupt";
    const SILENCE_LIMIT = 20000;
    const SPEECH_COOLDOWN = 800;

    function createVoice(elements, callbacks) {
        const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        let recognition = null;
        let recognitionGeneration = 0;
        let language = readLocalValue(LANGUAGE_KEY) === "en-IN" ? "en-IN" : "hi-IN";
        let interactionMode = readLocalValue(INTERACTION_KEY) === "push-to-talk" ? "push-to-talk" : "hands-free";
        let voiceMode = readSessionValue(MODE_KEY) === "true" && Boolean(Recognition) && interactionMode === "hands-free";
        let agentBusy = readSessionValue(BUSY_KEY) === "true";
        let allowVoiceInterrupt = readLocalValue(INTERRUPT_KEY) === "true";
        let voiceState = "idle";
        let isListening = false;
        let isSpeaking = false;
        let isStarting = false;
        let isMuted = false;
        let awaitingReply = false;
        let pttHeld = false;
        let pttSession = false;
        let pttReleaseRequested = false;
        let networkErrors = 0;
        let interruptRestarts = 0;
        let speechTurn = 0;
        let speechChunkToken = 0;
        let speechChunks = [];
        let speechChunkIndex = 0;
        let currentUtterance = null;
        let lastSpokenTexts = [];
        let lastUserText = "";
        let lastUserTime = 0;
        let userMessageTimes = [];
        let interruptTimer = null;
        let cooldownTimer = null;
        let speechWatchdog = null;
        let silenceTimer = null;
        let audioStream = null;
        let audioContext = null;
        let analyser = null;
        let animationFrame = null;
        let storageWarningShown = false;

        elements.language.value = language;
        elements.interactionMode.value = interactionMode;
        elements.allowVoiceInterrupt.checked = allowVoiceInterrupt;
        if (!voiceMode) saveSessionValue(MODE_KEY, "false");

        if (!Recognition) {
            elements.mic.hidden = true;
            elements.voiceMode.disabled = true;
            elements.voiceMode.title = "Voice recognition is unavailable. Use Chrome or Edge.";
            elements.status.textContent = "Voice input works in Chrome or Edge.";
        }

        if (!window.speechSynthesis || !window.SpeechSynthesisUtterance) {
            elements.mute.disabled = true;
            elements.mute.title = "Speech output is unavailable in this browser.";
        }

        elements.language.addEventListener("change", function () {
            setLanguage(elements.language.value);
        });
        elements.interactionMode.addEventListener("change", function () {
            setInteractionMode(elements.interactionMode.value);
        });
        elements.allowVoiceInterrupt.addEventListener("change", function () {
            setAllowVoiceInterrupt(elements.allowVoiceInterrupt.checked);
        });
        elements.mic.addEventListener("click", function (event) {
            if (interactionMode === "push-to-talk") {
                event.preventDefault();
                return;
            }
            if (isSpeaking) {
                interrupt();
            } else if (voiceState === "listening") {
                stopListening();
            } else {
                startListening(false, false);
            }
        });
        elements.mic.addEventListener("pointerdown", function (event) {
            if (interactionMode !== "push-to-talk" || event.button !== 0) return;
            event.preventDefault();
            pttHeld = true;
            pttSession = true;
            if (elements.mic.setPointerCapture) elements.mic.setPointerCapture(event.pointerId);
            startListening(false, true);
        });
        elements.mic.addEventListener("pointerup", releasePushToTalk);
        elements.mic.addEventListener("pointercancel", releasePushToTalk);
        elements.mic.addEventListener("lostpointercapture", releasePushToTalk);
        elements.voiceMode.addEventListener("click", function () {
            if (interactionMode !== "hands-free") {
                setStatus("Choose Hands-free mode to enable Voice mode.");
                return;
            }
            setVoiceMode(!voiceMode);
        });
        elements.mute.addEventListener("click", function () {
            isMuted = !isMuted;
            if (isMuted && isSpeaking) interruptSpeech(voiceMode);
            updateControls();
            if (voiceMode && !isMuted && !agentBusy && !awaitingReply) startListening(true, false);
        });
        elements.pill.addEventListener("click", function () {
            callbacks.openPanel();
            if (voiceMode && !isListening && !agentBusy && !awaitingReply)
                startListening(false, false);
        });
        document.addEventListener("keydown", handleKeyboardDown);
        document.addEventListener("keyup", handleKeyboardUp);
        window.addEventListener("pagehide", function () {
            resetVoiceState(false);
        });

        updateControls();
        updatePill();

        if (voiceMode) {
            window.setTimeout(function () {
                if (!voiceMode) return;
                if (agentBusy) {
                    setStatus("Parth is working. Tap to continue listening when ready.");
                    updatePill("Parth is working...");
                    return;
                }
                startListening(true, false);
            }, 0);
        }

        function setVoiceState(nextState) {
            const allowed = ["idle", "listening", "thinking", "speaking", "cooldown"];
            voiceState = allowed.indexOf(nextState) >= 0 ? nextState : "idle";
            if (voiceState !== "speaking") isSpeaking = false;
            if (voiceState !== "listening") isListening = false;
            callbacks.setVisualState(voiceState);
            elements.panel.classList.toggle("is-speaking", voiceState === "speaking");
            elements.panel.classList.toggle("is-cooldown", voiceState === "cooldown");
            updateControls();
            updatePill();
        }

        function createRecognition(interruptOnly) {
            if (!Recognition) return null;
            const generation = ++recognitionGeneration;
            const instance = new Recognition();
            recognition = instance;
            instance.continuous = true;
            instance.interimResults = true;
            instance.lang = language;
            instance.onstart = function () {
                if (!isCurrentRecognition(instance, generation)) return;
                if (interruptOnly) return;
                if (agentBusy || awaitingReply || isSpeaking || voiceState === "thinking" || voiceState === "cooldown") {
                    abortRecognition();
                    return;
                }
                isStarting = false;
                isListening = true;
                setVoiceState("listening");
                setStatus(pttSession ? "Hold to talk..." : "Listening...");
                beginSilenceTimer();
                drawWaveform();
                if (pttReleaseRequested || (pttSession && !pttHeld)) releasePushToTalk();
            };
            instance.onresult = function (event) {
                if (!isCurrentRecognition(instance, generation)) return;
                handleRecognitionResult(event, interruptOnly);
            };
            instance.onerror = function (event) {
                if (!isCurrentRecognition(instance, generation)) return;
                handleRecognitionError(event, interruptOnly);
            };
            instance.onend = function () {
                if (!isCurrentRecognition(instance, generation)) return;
                recognition = null;
                isStarting = false;
                isListening = false;
                stopAudioMonitor();
                updateControls();

                if (interruptOnly) {
                    if (isSpeaking && allowVoiceInterrupt && interruptRestarts < 3)
                        scheduleInterruptRecognition(speechTurn, 300);
                    return;
                }

                pttReleaseRequested = false;
                pttSession = false;
                if (voiceState !== "listening") return;
                setVoiceState("idle");
                if (voiceMode && !agentBusy && !awaitingReply)
                    scheduleListening(250);
                else if (interactionMode === "push-to-talk")
                    setStatus("Hold Mic or Space to talk.");
            };
            return instance;
        }

        function isCurrentRecognition(instance, generation) {
            return recognition === instance && recognitionGeneration === generation;
        }

        function handleRecognitionResult(event, interruptOnly) {
            if (interruptOnly) {
                if (voiceState !== "speaking" || !isSpeaking || !allowVoiceInterrupt) return;
                const interruptText = collectFinalText(event);
                if (interruptText) {
                    const normalized = normalizeForComparison(interruptText);
                    if (normalized.length >= 2 && !isEchoTranscript(interruptText))
                        interruptSpeechWithMessage(interruptText);
                }
                return;
            }

            if (voiceState !== "listening" || isSpeaking || agentBusy) return;

            const result = collectResult(event);
            const transcript = (result.finalText || result.interimText).trim();
            if (transcript) {
                resetSilenceTimer();
                elements.input.value = transcript;
                updatePill(transcript);
            }
            if (!result.finalText.trim()) return;

            const message = result.finalText.trim();
            const normalized = normalizeForComparison(message);
            if (normalized.length < 2) {
                elements.input.value = "";
                return;
            }
            if (isEchoTranscript(message)) {
                elements.input.value = "";
                return;
            }
            if (isRecentDuplicate(normalized)) {
                elements.input.value = "";
                if (voiceMode) pauseForEchoLoop();
                return;
            }

            elements.input.value = "";
            if (voiceMode && isStopCommand(message)) {
                setVoiceMode(false);
                callbacks.addMessage("user", message);
                setStatus("Voice mode stopped.");
                return;
            }

            submitVoiceMessage(message);
        }

        function collectResult(event) {
            let interimText = "";
            let finalText = "";
            for (let index = event.resultIndex; index < event.results.length; index += 1) {
                const result = event.results[index];
                if (result.isFinal) finalText += result[0].transcript;
                else interimText += result[0].transcript;
            }
            return { interimText: interimText, finalText: finalText };
        }

        function collectFinalText(event) {
            let text = "";
            for (let index = event.resultIndex; index < event.results.length; index += 1) {
                const result = event.results[index];
                if (result.isFinal) text += result[0].transcript;
            }
            return text.trim();
        }

        function handleRecognitionError(event, interruptOnly) {
            const error = event && event.error ? event.error : "unknown";
            if (interruptOnly) return;
            if (error === "no-speech") {
                if (!voiceMode) setStatus("No speech detected. Tap Mic to try again.");
                return;
            }
            if (error === "not-allowed" || error === "service-not-allowed" || error === "audio-capture") {
                setVoiceMode(false);
                callbacks.addMessage("assistant", "Microphone access was denied. Allow microphone access for this site in your browser's address bar, then tap Mic to try again.");
                return;
            }
            if (error === "network") {
                networkErrors += 1;
                if (voiceMode && networkErrors <= 1) {
                    setStatus("Speech service had a network error. Retrying once...");
                    return;
                }
                setVoiceMode(false);
                callbacks.addMessage("assistant", "Speech recognition needs an internet connection. Check your connection and try again.");
                return;
            }

            setVoiceMode(false);
            callbacks.addMessage("assistant", "Voice input stopped. Please try the microphone again.");
        }

        function startListening(automatic, pushToTalk) {
            if (!Recognition) {
                setStatus("Voice input works in Chrome or Edge.");
                return Promise.resolve(false);
            }
            if (agentBusy || awaitingReply || voiceState === "thinking") {
                setStatus("Parth is working...");
                return Promise.resolve(false);
            }
            if (isSpeaking) interruptSpeech(false);
            if (voiceState === "cooldown") {
                clearTimer(cooldownTimer);
                cooldownTimer = null;
            }
            if (voiceState === "listening" || isStarting) return Promise.resolve(true);

            abortRecognition();
            clearTimer(silenceTimer);
            silenceTimer = null;
            pttSession = Boolean(pushToTalk);
            pttReleaseRequested = false;
            isStarting = true;
            setVoiceState("idle");

            const instance = createRecognition(false);
            const generation = recognitionGeneration;
            const monitor = startAudioMonitor(generation);
            try {
                instance.start();
            } catch (error) {
                isStarting = false;
                recognition = null;
                recognitionGeneration += 1;
                stopAudioMonitor();
                handleStartFailure(error, automatic);
                return Promise.resolve(false);
            }
            return monitor.then(function () {
                if (generation !== recognitionGeneration) return false;
                if (isListening) drawWaveform();
                return true;
            }).catch(function (error) {
                if (generation !== recognitionGeneration) return false;
                abortRecognition();
                setVoiceState("idle");
                handleStartFailure(error, automatic);
                return false;
            });
        }

        function handleStartFailure(error, automatic) {
            if (automatic && error && (error.name === "NotAllowedError" || error.name === "InvalidStateError")) {
                setStatus("Tap to continue listening.");
                updatePill("Tap to continue listening");
                return;
            }
            if (error && (error.name === "NotAllowedError" || error.name === "PermissionDeniedError")) {
                setVoiceMode(false);
                callbacks.addMessage("assistant", "Microphone access was denied. Allow microphone access for this site in your browser's address bar, then tap Mic to try again.");
                return;
            }
            setStatus("Could not start voice input. Check microphone access and try again.");
        }

        function submitVoiceMessage(message) {
            awaitingReply = true;
            agentBusy = true;
            saveSessionValue(BUSY_KEY, "true");
            clearTimer(silenceTimer);
            silenceTimer = null;
            setVoiceState("thinking");
            abortRecognition();
            callbacks.sendText(message, "voice");
        }

        function onUserSubmitted(text, source) {
            const normalized = normalizeForComparison(text);
            if (!normalized) return true;
            const now = Date.now();
            const repeated = source === "voice" && normalized === lastUserText;
            if (voiceMode && repeated) {
                pauseForEchoLoop();
                return false;
            }

            if (voiceMode) {
                userMessageTimes = userMessageTimes.filter(function (time) { return now - time < 10000; });
                userMessageTimes.push(now);
                if (userMessageTimes.length > 3) {
                    pauseForEchoLoop();
                    return false;
                }
            }

            lastUserText = normalized;
            lastUserTime = now;
            awaitingReply = true;
            agentBusy = true;
            saveSessionValue(BUSY_KEY, "true");
            clearTimer(silenceTimer);
            silenceTimer = null;
            setVoiceState("thinking");
            abortRecognition();
            return true;
        }

        function isRecentDuplicate(normalized) {
            return normalized === lastUserText && Date.now() - lastUserTime < 3000;
        }

        function pauseForEchoLoop() {
            setVoiceMode(false);
            callbacks.addMessage("assistant", "Voice mode paused to avoid echo. Tap the mic to speak.");
        }

        function setVoiceMode(enabled) {
            const shouldEnable = Boolean(enabled) && Boolean(Recognition) && interactionMode === "hands-free";
            voiceMode = shouldEnable;
            saveSessionValue(MODE_KEY, voiceMode ? "true" : "false");
            if (!voiceMode) {
                clearTimer(silenceTimer);
                silenceTimer = null;
                if (voiceState === "listening") stopListening();
                else if (recognition && voiceState === "speaking") abortRecognition();
                else if (!isSpeaking && !agentBusy) setVoiceState("idle");
                setStatus("Voice mode is off.");
                updateControls();
                updatePill();
                return;
            }

            awaitingReply = false;
            networkErrors = 0;
            updateControls();
            if (!agentBusy) startListening(false, false);
        }

        function setLanguage(code) {
            language = code === "en-IN" ? "en-IN" : "hi-IN";
            elements.language.value = language;
            saveLocalValue(LANGUAGE_KEY, language);
            if (recognition && voiceState === "listening") {
                stopListening();
                if (voiceMode) scheduleListening(150);
            }
        }

        function setInteractionMode(mode) {
            interactionMode = mode === "push-to-talk" ? "push-to-talk" : "hands-free";
            elements.interactionMode.value = interactionMode;
            saveLocalValue(INTERACTION_KEY, interactionMode);
            if (interactionMode === "push-to-talk") {
                setVoiceMode(false);
                setStatus("Hold Mic or Space to talk.");
            }
            updateControls();
        }

        function setAllowVoiceInterrupt(enabled) {
            allowVoiceInterrupt = Boolean(enabled);
            elements.allowVoiceInterrupt.checked = allowVoiceInterrupt;
            saveLocalValue(INTERRUPT_KEY, allowVoiceInterrupt ? "true" : "false");
            if (isSpeaking && allowVoiceInterrupt) startInterruptRecognition(speechTurn);
            else if (!allowVoiceInterrupt && recognition && voiceState === "speaking") abortRecognition();
        }

        function setAgentState(state) {
            if (state === "thinking" || state === "acting") {
                agentBusy = true;
                awaitingReply = true;
                saveSessionValue(BUSY_KEY, "true");
                clearTimer(silenceTimer);
                silenceTimer = null;
                if (isSpeaking) resetVoiceState(false);
                else abortRecognition();
                setVoiceState("thinking");
                if (voiceMode) updatePill("Parth is working...");
                return;
            }
            if (state !== "idle") return;

            agentBusy = false;
            saveSessionValue(BUSY_KEY, "false");
            if (isSpeaking) {
                setVoiceState("speaking");
                return;
            }
            if (voiceState === "thinking") {
                if (awaitingReply) {
                    setVoiceState("idle");
                    setStatus("Waiting for Parth...");
                    return;
                }
                setVoiceState("idle");
            }
            if (voiceMode && !awaitingReply && voiceState === "idle")
                scheduleListening(250);
        }

        function speak(text, preserveTaskState) {
            if (!preserveTaskState) {
                awaitingReply = false;
                agentBusy = false;
                saveSessionValue(BUSY_KEY, "false");
            }
            clearTimer(silenceTimer);
            silenceTimer = null;

            const spokenText = cleanAssistantText(text);
            rememberSpokenText(spokenText);
            speechChunks = splitSpeechChunks(spokenText);
            speechChunkIndex = 0;
            interruptRestarts = 0;
            if (isMuted || speechChunks.length === 0 || !window.speechSynthesis || !window.SpeechSynthesisUtterance) {
                resetVoiceState(true);
                return;
            }

            clearTimer(cooldownTimer);
            cooldownTimer = null;
            const turn = ++speechTurn;
            speakNextChunk(turn);
        }

        function speakNotice(text) {
            if (!voiceMode) return;
            agentBusy = true;
            awaitingReply = true;
            saveSessionValue(BUSY_KEY, "true");
            speak(text, true);
        }

        function speakNextChunk(turn) {
            if (turn !== speechTurn) return;
            if (speechChunkIndex >= speechChunks.length) {
                finishSpeech(turn);
                return;
            }

            const text = speechChunks[speechChunkIndex];
            const chunkToken = ++speechChunkToken;
            abortRecognition();
            isSpeaking = true;
            setVoiceState("speaking");
            const utterance = new window.SpeechSynthesisUtterance(text);
            currentUtterance = utterance;
            utterance.lang = language;
            const selectedVoice = chooseVoice(language);
            if (selectedVoice) utterance.voice = selectedVoice;
            utterance.onstart = function () {
                if (turn !== speechTurn) return;
                setVoiceState("speaking");
                if (allowVoiceInterrupt) startInterruptRecognition(turn);
            };
            utterance.onend = function () {
                finishSpeechChunk(turn, chunkToken);
            };
            utterance.onerror = function () {
                finishSpeechChunk(turn, chunkToken);
            };

            const expectedMs = (text.length / 12 + 1.5) * 1000;
            clearTimer(speechWatchdog);
            speechWatchdog = window.setTimeout(function () {
                if (turn !== speechTurn || chunkToken !== speechChunkToken) return;
                speechChunkToken += 1;
                try { window.speechSynthesis.cancel(); } catch (error) { }
                currentUtterance = null;
                speechWatchdog = null;
                speechChunkIndex += 1;
                speakNextChunk(turn);
            }, expectedMs);

            try {
                window.speechSynthesis.cancel();
                window.speechSynthesis.speak(utterance);
            } catch (error) {
                finishSpeech(turn);
            }
        }

        function finishSpeechChunk(turn, chunkToken) {
            if (turn !== speechTurn || chunkToken !== speechChunkToken) return;
            speechChunkToken += 1;
            clearTimer(speechWatchdog);
            speechWatchdog = null;
            currentUtterance = null;
            speechChunkIndex += 1;
            speakNextChunk(turn);
        }

        function finishSpeech(turn) {
            if (turn !== speechTurn) return;
            try {
                clearTimer(speechWatchdog);
                speechWatchdog = null;
                if (window.speechSynthesis) window.speechSynthesis.cancel();
            } finally {
                resetVoiceState(true);
            }
        }

        function resetVoiceState(resumeAfterSpeech) {
            speechTurn += 1;
            clearTimer(speechWatchdog);
            clearTimer(cooldownTimer);
            clearTimer(interruptTimer);
            speechWatchdog = null;
            cooldownTimer = null;
            interruptTimer = null;
            currentUtterance = null;
            speechChunks = [];
            speechChunkIndex = 0;
            speechChunkToken += 1;

            try {
                abortRecognition();
                if (window.speechSynthesis) window.speechSynthesis.cancel();
            } finally {
                isSpeaking = false;
                isListening = false;
                isStarting = false;
                if (agentBusy) {
                    setVoiceState("thinking");
                    return;
                }
                if (resumeAfterSpeech) {
                    setVoiceState("cooldown");
                    cooldownTimer = window.setTimeout(function () {
                        cooldownTimer = null;
                        if (agentBusy) {
                            setVoiceState("thinking");
                            return;
                        }
                        if (voiceMode && !awaitingReply && interactionMode === "hands-free")
                            startListening(true, false);
                        else
                            setVoiceState("idle");
                    }, SPEECH_COOLDOWN);
                } else {
                    setVoiceState("idle");
                }
            }
        }

        function interrupt() {
            if (isSpeaking) {
                interruptSpeech(false);
                return startListening(false, interactionMode === "push-to-talk");
            }
            if (voiceState === "cooldown") {
                clearTimer(cooldownTimer);
                cooldownTimer = null;
                setVoiceState("idle");
                return startListening(false, interactionMode === "push-to-talk");
            }
            return startListening(false, interactionMode === "push-to-talk");
        }

        function interruptSpeechWithMessage(text) {
            resetVoiceState(false);
            awaitingReply = false;
            submitVoiceMessage(text);
        }

        function interruptSpeech(resumeListening) {
            awaitingReply = false;
            agentBusy = false;
            resetVoiceState(false);
            if (resumeListening) startListening(false, interactionMode === "push-to-talk");
        }

        function abortRecognition() {
            recognitionGeneration += 1;
            const current = recognition;
            recognition = null;
            isStarting = false;
            isListening = false;
            pttReleaseRequested = false;
            if (current) {
                current.onstart = null;
                current.onresult = null;
                current.onerror = null;
                current.onend = null;
                try { current.abort(); } catch (error) { }
            }
            stopAudioMonitor();
        }

        function stopListening() {
            clearTimer(silenceTimer);
            silenceTimer = null;
            abortRecognition();
            if (!agentBusy && !isSpeaking) setVoiceState("idle");
            if (voiceMode) setStatus("Tap Mic to continue listening.");
        }

        function releasePushToTalk() {
            if (!pttHeld && !pttReleaseRequested) return;
            pttHeld = false;
            if (!recognition || voiceState !== "listening") {
                pttReleaseRequested = true;
                return;
            }
            pttReleaseRequested = true;
            setStatus("Finishing your voice message...");
            try { recognition.stop(); }
            catch (error) {
                pttReleaseRequested = false;
                stopListening();
            }
        }

        function beginPushToTalk() {
            if (interactionMode !== "push-to-talk") return;
            if (voiceMode) setVoiceMode(false);
            pttHeld = true;
            pttSession = true;
            if (isSpeaking) interruptSpeech(false);
            startListening(false, true);
        }

        function handleKeyboardDown(event) {
            const key = event.key;
            if (isSpeaking && (key === "Escape" || key === " " || event.code === "Space")) {
                if (isEditableTarget(event.target) && key !== "Escape") return;
                event.preventDefault();
                interrupt();
                return;
            }
            if (key !== " " && event.code !== "Space" || interactionMode !== "push-to-talk") return;
            if (!elements.panel.contains(document.activeElement) || isEditableTarget(event.target)) return;
            if (event.repeat || pttHeld) return;
            event.preventDefault();
            beginPushToTalk();
        }

        function handleKeyboardUp(event) {
            if ((event.key === " " || event.code === "Space") && interactionMode === "push-to-talk" &&
                pttHeld && elements.panel.contains(document.activeElement)) {
                event.preventDefault();
                releasePushToTalk();
            }
        }

        function isEditableTarget(target) {
            return target && (target.isContentEditable ||
                /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName) ||
                target.closest && target.closest("[contenteditable='true']"));
        }

        function startInterruptRecognition(turn) {
            if (!Recognition || !allowVoiceInterrupt || !isSpeaking || turn !== speechTurn || recognition) return;
            if (interruptRestarts >= 3) return;
            interruptRestarts += 1;
            const instance = createRecognition(true);
            try { instance.start(); }
            catch (error) {
                if (recognition === instance) recognition = null;
            }
        }

        function scheduleInterruptRecognition(turn, delay) {
            clearTimer(interruptTimer);
            interruptTimer = window.setTimeout(function () {
                interruptTimer = null;
                startInterruptRecognition(turn);
            }, delay);
        }

        function isEchoTranscript(text) {
            const normalized = normalizeForComparison(text);
            if (normalized.length < 2) return true;
            return lastSpokenTexts.some(function (spoken) {
                if (!spoken) return false;
                return spoken.indexOf(normalized) >= 0 || tokenJaccard(normalized, spoken) > .6;
            });
        }

        function tokenJaccard(left, right) {
            const leftTokens = new Set(left.split(" ").filter(Boolean));
            const rightTokens = new Set(right.split(" ").filter(Boolean));
            if (leftTokens.size === 0 || rightTokens.size === 0) return 0;
            let overlap = 0;
            leftTokens.forEach(function (token) {
                if (rightTokens.has(token)) overlap += 1;
            });
            return overlap / (leftTokens.size + rightTokens.size - overlap);
        }

        function normalizeForComparison(text) {
            return String(text || "")
                .toLocaleLowerCase()
                .replace(/[\p{P}\p{S}]/gu, " ")
                .replace(/\s+/g, " ")
                .trim();
        }

        function rememberSpokenText(text) {
            const normalized = normalizeForComparison(text);
            if (!normalized) return;
            lastSpokenTexts.push(normalized);
            if (lastSpokenTexts.length > 3) lastSpokenTexts.shift();
        }

        function isStopCommand(text) {
            const normalized = normalizeForComparison(text);
            return /^(stop|stop listening|band karo|band kar do|बंद करो|बस|रुको)$/.test(normalized);
        }

        function startAudioMonitor(generation) {
            if (!window.navigator.mediaDevices || !window.navigator.mediaDevices.getUserMedia)
                return Promise.reject(new Error("Microphone access is not available."));
            if (audioStream) return Promise.resolve();

            return window.navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
                if (generation !== recognitionGeneration) {
                    stream.getTracks().forEach(function (track) { track.stop(); });
                    return;
                }
                audioStream = stream;
                const AudioContext = window.AudioContext || window.webkitAudioContext;
                if (!AudioContext) return;
                audioContext = new AudioContext();
                analyser = audioContext.createAnalyser();
                analyser.fftSize = 128;
                audioContext.createMediaStreamSource(stream).connect(analyser);
                return audioContext.resume();
            });
        }

        function stopAudioMonitor() {
            if (animationFrame !== null) window.cancelAnimationFrame(animationFrame);
            animationFrame = null;
            if (audioStream) audioStream.getTracks().forEach(function (track) { track.stop(); });
            audioStream = null;
            analyser = null;
            if (audioContext) {
                audioContext.close().catch(function () { warnStorage(); });
                audioContext = null;
            }
            drawCanvas(elements.waveform, null, 0);
            drawCanvas(elements.pillWaveform, null, 0);
            elements.orb.style.setProperty("--hrms-voice-scale", "1");
        }

        function drawWaveform() {
            if (!analyser || !isListening || voiceState !== "listening") return;
            const samples = new Uint8Array(analyser.frequencyBinCount);
            analyser.getByteFrequencyData(samples);
            const volume = samples.reduce(function (sum, value) { return sum + value; }, 0) / (samples.length * 255);
            drawCanvas(elements.waveform, samples, volume);
            drawCanvas(elements.pillWaveform, samples, volume);
            elements.orb.style.setProperty("--hrms-voice-scale", String(1 + Math.min(.55, volume * 1.6)));
            animationFrame = window.requestAnimationFrame(drawWaveform);
        }

        function drawCanvas(canvas, samples, volume) {
            const context = canvas.getContext("2d");
            if (!context) return;
            const ratio = window.devicePixelRatio || 1;
            const width = canvas.clientWidth || 180;
            const height = canvas.clientHeight || 30;
            if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
                canvas.width = Math.round(width * ratio);
                canvas.height = Math.round(height * ratio);
            }
            context.setTransform(ratio, 0, 0, ratio, 0, 0);
            context.clearRect(0, 0, width, height);
            if (!samples) return;
            const bars = Math.min(32, samples.length);
            const gap = 2;
            const barWidth = Math.max(2, (width - gap * (bars - 1)) / bars);
            const gradient = context.createLinearGradient(0, 0, width, 0);
            gradient.addColorStop(0, "#4387ff");
            gradient.addColorStop(.52, "#9259f6");
            gradient.addColorStop(1, "#ed62c6");
            context.fillStyle = gradient;
            for (let index = 0; index < bars; index += 1) {
                const magnitude = samples[Math.floor(index * samples.length / bars)] / 255;
                const barHeight = Math.max(3, height * Math.min(1, magnitude * 1.8 + volume * .12));
                const x = index * (barWidth + gap);
                context.beginPath();
                context.roundRect(x, (height - barHeight) / 2, barWidth, barHeight, barWidth / 2);
                context.fill();
            }
        }

        function beginSilenceTimer() {
            if (!voiceMode || silenceTimer !== null) return;
            silenceTimer = window.setTimeout(function () {
                silenceTimer = null;
                setVoiceMode(false);
                callbacks.addMessage("assistant", "Voice mode paused after 20 seconds of silence.");
            }, SILENCE_LIMIT);
        }

        function resetSilenceTimer() {
            clearTimer(silenceTimer);
            silenceTimer = null;
            beginSilenceTimer();
        }

        function scheduleListening(delay) {
            clearTimer(cooldownTimer);
            cooldownTimer = window.setTimeout(function () {
                cooldownTimer = null;
                if (voiceMode && !agentBusy && !awaitingReply && !isSpeaking)
                    startListening(true, false);
            }, delay);
        }

        function updateControls() {
            elements.voiceMode.setAttribute("aria-pressed", voiceMode ? "true" : "false");
            elements.voiceMode.classList.toggle("is-active", voiceMode);
            elements.voiceMode.disabled = !Recognition || interactionMode !== "hands-free";
            elements.voiceMode.textContent = voiceMode ? "◉ Voice mode on" : "◉ Voice mode";
            elements.mute.setAttribute("aria-pressed", isMuted ? "true" : "false");
            elements.mute.textContent = isMuted ? "Unmute" : "Mute";
            elements.mute.title = isMuted ? "Turn Parth voice on" : "Mute Parth voice";
            elements.mic.setAttribute("aria-label", interactionMode === "push-to-talk"
                ? "Hold to talk" : isListening || isStarting ? "Stop voice input" : "Start voice input");
            elements.mic.textContent = interactionMode === "push-to-talk"
                ? pttHeld ? "Release to send" : "Hold" : isListening || isStarting ? "Stop" : "Mic";
            elements.interactionMode.value = interactionMode;
            elements.allowVoiceInterrupt.checked = allowVoiceInterrupt;
            updatePill();
        }

        function updatePill(transcript) {
            const visible = voiceMode && !elements.panel.classList.contains("is-open");
            const label = transcript || (voiceState === "listening" ? "Listening..." :
                voiceState === "thinking" ? "Parth is working..." :
                    voiceState === "speaking" ? "Parth is speaking..." : "Voice mode on");
            callbacks.updatePill(visible, label);
        }

        function setStatus(text) {
            elements.status.textContent = text;
            updatePill();
        }

        function chooseVoice(selectedLanguage) {
            const synthesis = window.speechSynthesis;
            if (!synthesis) return null;
            const voices = synthesis.getVoices();
            const preferred = voices.find(function (voice) {
                return voice.lang && voice.lang.toLowerCase() === selectedLanguage.toLowerCase();
            });
            if (preferred) return preferred;
            const hindi = voices.find(function (voice) { return voice.lang && voice.lang.toLowerCase().startsWith("hi-"); });
            if (hindi) return hindi;
            return voices.find(function (voice) { return voice.lang && voice.lang.toLowerCase().startsWith("en-in"); }) || null;
        }

        function cleanAssistantText(text) {
            return String(text || "")
                .replace(/```[\s\S]*?```/g, function (block) { return block.replace(/```[^`]*```/g, " "); })
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

        function splitSpeechChunks(text) {
            if (!text) return [];
            const sentences = text.match(/[^.!?।]+[.!?।]+|[^.!?।]+$/g) || [text];
            const chunks = [];
            sentences.forEach(function (sentence) {
                const trimmed = sentence.trim();
                if (!trimmed) return;
                if (trimmed.length <= 140) {
                    chunks.push(trimmed);
                    return;
                }
                let part = "";
                trimmed.split(/\s+/).forEach(function (word) {
                    if (part && (part + " " + word).length > 140) {
                        chunks.push(part);
                        part = word;
                    } else {
                        part = part ? part + " " + word : word;
                    }
                });
                if (part) chunks.push(part);
            });
            return chunks;
        }

        function readLocalValue(key) {
            try { return window.localStorage.getItem(key); }
            catch (error) { warnStorage(); return null; }
        }

        function saveLocalValue(key, value) {
            try { window.localStorage.setItem(key, value); }
            catch (error) { warnStorage(); }
        }

        function readSessionValue(key) {
            try { return window.sessionStorage.getItem(key); }
            catch (error) { warnStorage(); return null; }
        }

        function saveSessionValue(key, value) {
            try { window.sessionStorage.setItem(key, value); }
            catch (error) { warnStorage(); }
        }

        function warnStorage() {
            if (storageWarningShown) return;
            storageWarningShown = true;
            console.warn("Parth encountered a browser storage or audio cleanup issue.");
        }

        function clearTimer(timer) {
            if (timer !== null) window.clearTimeout(timer);
        }

        return {
            startListening: function () { return startListening(false, false); },
            stopListening: stopListening,
            speak: speak,
            speakNotice: speakNotice,
            setVoiceMode: setVoiceMode,
            setLanguage: setLanguage,
            setAgentState: setAgentState,
            onUserSubmitted: onUserSubmitted,
            refreshPill: updatePill,
            interrupt: interrupt,
            isSpeaking: function () { return isSpeaking; }
        };
    }

    window.HrmsAssistantVoice = { create: createVoice };
})(window, document);
