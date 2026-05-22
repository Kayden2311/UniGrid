/* UniGrid Calendar Scheduling Component Engine */

function scheduleComponent() {
    return {
        weekOffset: 0,
        events: [],
        init() {
            // Load serialized C# Razor Page models
            let rawEvents = window.scheduleRawEvents || [];
            this.events = rawEvents.map(e => {
                let startDate = new Date(e.startTime);
                let endDate = new Date(e.endTime);
                let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                let startSlot = Math.max(0, (startDate.getHours() - 7) * 2 + (startDate.getMinutes() >= 30 ? 1 : 0));
                let endSlot = Math.min(34, (endDate.getHours() - 7) * 2 + (endDate.getMinutes() >= 30 ? 1 : 0));
                let duration = Math.max(1, endSlot - startSlot);

                let descText = e.description || '';
                let priority = 'medium';
                let colorIdx = 0;
                try {
                    if (descText.startsWith('{')) {
                        let descObj = JSON.parse(descText);
                        descText = descObj.desc || '';
                        priority = descObj.priority || 'medium';
                        colorIdx = descObj.color !== undefined ? descObj.color : 0;
                    }
                } catch (err) {}

                return {
                    id: e.id,
                    title: e.title,
                    description: descText,
                    dayIdx: dayIdx,
                    startSlot: startSlot,
                    duration: duration,
                    priority: priority,
                    colorIdx: colorIdx,
                    startDate: startDate,
                    endDate: endDate
                };
            });
        },
        tasks: window.scheduleRawTasks || [],
        dialogOpen: false,
        isEdit: false,
        mode: 'idle', // 'idle', 'creating', 'moving', 'resizing-top', 'resizing-bottom'
        dragCreate: null, // { dayIdx, startSlot, endSlot }
        movingTask: null, // { taskId, offsetSlot, currentDayIdx, currentStartSlot }
        resizingTask: null, // { taskId, edge, origStartSlot, origDuration, startY }
        
        // Physics and gesture drag variables
        dragStartX: 0,
        dragStartY: 0,
        dragTranslateX: 0,
        dragTranslateY: 0,
        dragMoved: false,

        // Form fields
        formId: '',
        formTitle: '',
        formDesc: '',
        formStartTime: '',
        formEndTime: '',
        formColor: 0,
        formPriority: 'medium',
        formDayIdx: 0,
        formStartSlot: 0,
        formDuration: 2,

        get weekDates() {
            return this.getWeekDates(this.weekOffset);
        },

        get weekLabel() {
            let dates = this.weekDates;
            let opt = { month: 'short', day: 'numeric' };
            return 'Week of ' + dates[0].toLocaleDateString('en-US', opt);
        },

        isToday(dayIdx) {
            let d = this.weekDates[dayIdx];
            return new Date().toDateString() === d.toDateString();
        },

        isCurrentHour(slot) {
            let now = new Date();
            let hour = now.getHours();
            let halfHour = now.getMinutes() >= 30 ? 1 : 0;
            return (7 + Math.floor(slot / 2)) === hour && (slot % 2 === halfHour);
        },

        getWeekDates(offset) {
            let now = new Date();
            let monday = new Date(now);
            let diff = now.getDay() === 0 ? -6 : 1 - now.getDay();
            monday.setDate(now.getDate() + diff + offset * 7);
            let dates = [];
            for (let i = 0; i < 7; i++) {
                let d = new Date(monday);
                d.setDate(monday.getDate() + i);
                dates.push(d);
            }
            return dates;
        },

        getEventsForDay(dayIdx) {
            let targetDate = this.weekDates[dayIdx];
            if (!targetDate) return [];
            let dayEvents = this.events.filter(e => {
                let d = new Date(e.startDate);
                return d.getFullYear() === targetDate.getFullYear() &&
                       d.getMonth() === targetDate.getMonth() &&
                       d.getDate() === targetDate.getDate();
            });

            // Sort dayEvents chronologically by startSlot, then by longer durations
            dayEvents.sort((a, b) => a.startSlot - b.startSlot || b.duration - a.duration);

            // Group overlapping events in the day
            let groups = [];
            for (let ev of dayEvents) {
                let matchedGroup = null;
                for (let g of groups) {
                    if (ev.startSlot < g.endSlot) {
                        matchedGroup = g;
                        break;
                    }
                }
                if (!matchedGroup) {
                    matchedGroup = { endSlot: ev.startSlot + ev.duration, events: [] };
                    groups.push(matchedGroup);
                } else {
                    matchedGroup.endSlot = Math.max(matchedGroup.endSlot, ev.startSlot + ev.duration);
                }
                matchedGroup.events.push(ev);
            }

            // Assign virtual columns and properties within each group
            for (let g of groups) {
                let groupCols = [];
                for (let ev of g.events) {
                    let colIdx = 0;
                    while (colIdx < groupCols.length) {
                        let lastEvInCol = groupCols[colIdx][groupCols[colIdx].length - 1];
                        if (ev.startSlot >= lastEvInCol.startSlot + lastEvInCol.duration) {
                            break;
                        }
                        colIdx++;
                    }
                    if (colIdx === groupCols.length) {
                        groupCols.push([]);
                    }
                    groupCols[colIdx].push(ev);
                    ev.colIdx = colIdx;
                }
                let numCols = groupCols.length;
                for (let ev of g.events) {
                    ev.left = (ev.colIdx / numCols) * 100;
                    ev.width = 100 / numCols;
                }
            }

            return dayEvents;
        },

        get weeklyDeadlines() {
            let dates = this.weekDates;
            let startOfWeek = new Date(dates[0]);
            startOfWeek.setHours(0, 0, 0, 0);
            let endOfWeek = new Date(dates[6]);
            endOfWeek.setHours(23, 59, 59, 999);
            
            return this.tasks.filter(t => {
                if (!t.dueDate) return false;
                let dDate = new Date(t.dueDate);
                return dDate >= startOfWeek && dDate <= endOfWeek;
            }).map(t => {
                let dDate = new Date(t.dueDate);
                let options = { weekday: 'short', hour: '2-digit', minute: '2-digit' };
                return {
                    id: t.id,
                    title: t.title,
                    workspaceName: t.workspaceName,
                    formattedDate: dDate.toLocaleDateString('en-US', options),
                    priority: t.priority
                };
            });
        },

        slotToTime(slot) {
            let h = 7 + Math.floor(slot / 2);
            let m = slot % 2 === 0 ? '00' : '30';
            let suffix = h >= 12 ? 'PM' : 'AM';
            let displayHour = h % 12;
            if (displayHour === 0) displayHour = 12;
            return displayHour.toString().padStart(2, '0') + ':' + m + ' ' + suffix;
        },

        slotsToISOTimes(dayIdx, startSlot, duration) {
            let date = this.weekDates[dayIdx];
            let sh = 7 + Math.floor(startSlot / 2);
            let sm = startSlot % 2 === 0 ? 0 : 30;
            
            let startDate = new Date(date);
            startDate.setHours(sh, sm, 0, 0);
            
            let endDate = new Date(startDate.getTime() + duration * 30 * 60000);
            
            return {
                startTime: startDate.toISOString(),
                endTime: endDate.toISOString()
            };
        },

        openAdd(dayIdx, slotIdx, durationSlotCount = 2) {
            this.isEdit = false;
            this.formId = '';
            this.formTitle = '';
            this.formDesc = '';
            this.formPriority = 'medium';
            this.formColor = 0;
            this.formDayIdx = dayIdx;
            this.formStartSlot = slotIdx;
            this.formDuration = durationSlotCount;
            
            let { startTime, endTime } = this.slotsToISOTimes(dayIdx, slotIdx, durationSlotCount);
            this.formStartTime = startTime;
            this.formEndTime = endTime;
            this.dialogOpen = true;
        },

        openEdit(event) {
            this.isEdit = true;
            this.formId = event.id;
            this.formTitle = event.title;
            this.formDesc = event.description;
            this.formPriority = event.priority;
            this.formColor = event.colorIdx;
            this.formDayIdx = event.dayIdx;
            this.formStartSlot = event.startSlot;
            this.formDuration = event.duration;
            
            let { startTime, endTime } = this.slotsToISOTimes(event.dayIdx, event.startSlot, event.duration);
            this.formStartTime = startTime;
            this.formEndTime = endTime;
            this.dialogOpen = true;
        },

        handleCellMouseDown(dayIdx, slotIdx) {
            if (this.mode !== 'idle') return;
            this.mode = 'creating';
            this.dragCreate = { dayIdx: dayIdx, startSlot: slotIdx, endSlot: slotIdx };

            let gridContainer = document.getElementById('grid-container');
            if (!gridContainer) return;

            let onMove = (evMouse) => {
                if (this.mode !== 'creating' || !this.dragCreate) return;
                
                let rectContainer = gridContainer.getBoundingClientRect();
                let scrollContainer = gridContainer.closest('.overflow-auto');
                let scrollTop = scrollContainer ? scrollContainer.scrollTop : 0;
                let scrollLeft = scrollContainer ? scrollContainer.scrollLeft : 0;
                
                let relX = evMouse.clientX - rectContainer.left + scrollLeft;
                let relY = evMouse.clientY - rectContainer.top + scrollTop;
                
                let dayWidth = (rectContainer.width - 64) / 7;
                let day = 0;
                if (relX >= 64) {
                    day = Math.floor((relX - 64) / dayWidth);
                }
                day = Math.max(0, Math.min(day, 6));
                
                let slotIdx = Math.floor(relY / 48);
                slotIdx = Math.max(0, Math.min(slotIdx, 33));
                
                this.dragCreate.endSlot = slotIdx;
            };

            let onUp = () => {
                if (this.mode === 'creating' && this.dragCreate) {
                    let startSlot = Math.min(this.dragCreate.startSlot, this.dragCreate.endSlot);
                    let endSlot = Math.max(this.dragCreate.startSlot, this.dragCreate.endSlot);
                    let duration = endSlot - startSlot + 1;
                    if (duration >= 1) {
                        this.openAdd(this.dragCreate.dayIdx, startSlot, duration);
                    }
                }
                this.mode = 'idle';
                this.dragCreate = null;
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            };

            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        },

        handleCellMouseEnter(dayIdx, slotIdx) {
            // Managed seamlessly
        },

        async handleMouseUp() {
            // Managed seamlessly
        },

        handleMouseLeave() {
            // Managed seamlessly
        },

        startMoveTask(e, ev, dayIdx) {
            let gridContainer = document.getElementById('grid-container');
            if (!gridContainer) return;
            
            this.dragStartX = e.clientX;
            this.dragStartY = e.clientY;
            this.dragTranslateX = 0;
            this.dragTranslateY = 0;
            this.dragMoved = false;

            let cardEl = document.getElementById('ev-' + ev.id);
            let rectCard = cardEl.getBoundingClientRect();
            let offsetY = e.clientY - rectCard.top;
            let offsetSlot = Math.floor(offsetY / 48);

            this.mode = 'moving';
            this.movingTask = {
                taskId: ev.id,
                offsetSlot: offsetSlot,
                currentDayIdx: dayIdx,
                currentStartSlot: ev.startSlot
            };

            let onMove = (evMouse) => {
                if (this.mode !== 'moving' || !this.movingTask) return;

                let dx = evMouse.clientX - this.dragStartX;
                let dy = evMouse.clientY - this.dragStartY;

                // Update physical offset for absolute free-pixel coordinate moving
                this.dragTranslateX = dx;
                this.dragTranslateY = dy;

                if (Math.abs(dx) > 4 || Math.abs(dy) > 4) {
                    this.dragMoved = true;
                }

                let rectContainer = gridContainer.getBoundingClientRect();
                let sContainer = gridContainer.closest('.overflow-auto');
                let sTop = sContainer ? sContainer.scrollTop : 0;
                let sLeft = sContainer ? sContainer.scrollLeft : 0;

                let relX = evMouse.clientX - rectContainer.left + sLeft;
                let relY = evMouse.clientY - rectContainer.top + sTop;

                let dayWidth = (rectContainer.width - 64) / 7;
                let day = 0;
                if (relX >= 64) {
                    day = Math.floor((relX - 64) / dayWidth);
                }
                day = Math.max(0, Math.min(day, 6));

                let slotIdx = Math.floor(relY / 48);
                slotIdx = Math.max(0, Math.min(slotIdx, 33));

                let targetEv = this.events.find(x => x.id === this.movingTask.taskId);
                if (targetEv) {
                    let newStart = Math.max(0, Math.min(slotIdx - this.movingTask.offsetSlot, 34 - targetEv.duration));
                    this.movingTask.currentDayIdx = day;
                    this.movingTask.currentStartSlot = newStart;
                }
            };

            let onUp = async () => {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);

                if (this.mode === 'moving' && this.movingTask) {
                    let targetEv = this.events.find(x => x.id === this.movingTask.taskId);
                    if (targetEv && this.dragMoved) {
                        // Drag completed, snap card in database and local array
                        targetEv.dayIdx = this.movingTask.currentDayIdx;
                        targetEv.startSlot = this.movingTask.currentStartSlot;

                        let { startTime, endTime } = this.slotsToISOTimes(targetEv.dayIdx, targetEv.startSlot, targetEv.duration);
                        targetEv.startDate = new Date(startTime);
                        targetEv.endDate = new Date(endTime);
                        await this.updateEventTimeInDb(targetEv.id, startTime, endTime);
                    } else if (targetEv && !this.dragMoved) {
                        // Mere click with zero coordinate move - open edit dialog
                        this.openEdit(targetEv);
                    }
                }

                this.mode = 'idle';
                this.movingTask = null;
                this.dragTranslateX = 0;
                this.dragTranslateY = 0;
                this.dragMoved = false;
            };

            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        },

        startResize(e, ev, edge) {
            this.mode = edge === 'top' ? 'resizing-top' : 'resizing-bottom';
            this.resizingTask = {
                taskId: ev.id,
                edge: edge,
                origStartSlot: ev.startSlot,
                origDuration: ev.duration,
                startY: e.clientY
            };

            let onMove = (evMouse) => {
                let dy = evMouse.clientY - e.clientY;
                let slotDelta = Math.round(dy / 48);
                let targetEv = this.events.find(x => x.id === ev.id);
                if (!targetEv) return;

                if (edge === 'bottom') {
                    let newDuration = Math.max(1, this.resizingTask.origDuration + slotDelta);
                    targetEv.duration = Math.min(newDuration, 34 - targetEv.startSlot);
                } else {
                    let newStart = Math.max(0, this.resizingTask.origStartSlot + slotDelta);
                    let endSlot = this.resizingTask.origStartSlot + this.resizingTask.origDuration;
                    if (newStart >= endSlot) newStart = endSlot - 1;
                    targetEv.startSlot = newStart;
                    targetEv.duration = Math.max(1, endSlot - newStart);
                }
            };

            let onUp = async () => {
                this.mode = 'idle';
                let targetEv = this.events.find(x => x.id === ev.id);
                if (targetEv) {
                    let { startTime, endTime } = this.slotsToISOTimes(targetEv.dayIdx, targetEv.startSlot, targetEv.duration);
                    targetEv.startDate = new Date(startTime);
                    targetEv.endDate = new Date(endTime);
                    await this.updateEventTimeInDb(targetEv.id, startTime, endTime);
                }
                this.resizingTask = null;
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            };

            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        },

        async updateEventTimeInDb(eventId, startTime, endTime) {
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let payload = new URLSearchParams();
            payload.append('eventId', eventId);
            payload.append('startTime', startTime);
            payload.append('endTime', endTime);
            payload.append('__RequestVerificationToken', token);

            try {
                let response = await fetch('?handler=UpdateEventTime', {
                    method: 'POST',
                    body: payload
                });
                if (!response.ok) {
                    console.error("Failed to update event times in database");
                }
            } catch (err) {
                console.error("Network error updating event times:", err);
            }
        },

        get createPreview() {
            if (this.mode !== 'creating' || !this.dragCreate) return null;
            let startSlot = Math.min(this.dragCreate.startSlot, this.dragCreate.endSlot);
            let endSlot = Math.max(this.dragCreate.startSlot, this.dragCreate.endSlot);
            return {
                dayIdx: this.dragCreate.dayIdx,
                startSlot: startSlot,
                duration: endSlot - startSlot + 1
            };
        },

        saveEvent() {
            if (!this.formTitle.trim()) return;
            
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let payload = new URLSearchParams();
            
            let descJson = JSON.stringify({
                desc: this.formDesc,
                priority: this.formPriority,
                color: this.formColor
            });
            
            let { startTime, endTime } = this.slotsToISOTimes(this.formDayIdx, this.formStartSlot, this.formDuration);
            this.formStartTime = startTime;
            this.formEndTime = endTime;
            
            payload.append('title', this.formTitle);
            payload.append('description', descJson);
            payload.append('startTime', this.formStartTime);
            payload.append('endTime', this.formEndTime);
            payload.append('__RequestVerificationToken', token);
            
            if (this.isEdit) {
                payload.append('eventId', this.formId);
                fetch('?handler=EditEvent', {
                    method: 'POST',
                    body: payload
                })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        let idx = this.events.findIndex(e => e.id === this.formId);
                        if (idx !== -1) {
                            let e = data.eventItem;
                            let startDate = new Date(e.startTime);
                            let endDate = new Date(e.endTime);
                            let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                            let startSlot = Math.max(0, (startDate.getHours() - 7) * 2 + (startDate.getMinutes() >= 30 ? 1 : 0));
                            let endSlot = Math.min(34, (endDate.getHours() - 7) * 2 + (endDate.getMinutes() >= 30 ? 1 : 0));
                            let duration = Math.max(1, endSlot - startSlot);

                            let descText = e.description || '';
                            let priority = 'medium';
                            let colorIdx = 0;
                            try {
                                if (descText.startsWith('{')) {
                                    let descObj = JSON.parse(descText);
                                    descText = descObj.desc || '';
                                    priority = descObj.priority || 'medium';
                                    colorIdx = descObj.color !== undefined ? descObj.color : 0;
                                }
                            } catch (err) {}

                            this.events[idx] = {
                                id: e.id,
                                title: e.title,
                                description: descText,
                                dayIdx: dayIdx,
                                startSlot: startSlot,
                                duration: duration,
                                priority: priority,
                                colorIdx: colorIdx,
                                startDate: startDate,
                                endDate: endDate
                            };
                        }
                        this.dialogOpen = false;
                    }
                })
                .catch(err => console.error("Error saving event:", err));
            } else {
                fetch('?handler=CreateEvent', {
                    method: 'POST',
                    body: payload
                })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        let e = data.eventItem;
                        let startDate = new Date(e.startTime);
                        let endDate = new Date(e.endTime);
                        let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                        let startSlot = Math.max(0, (startDate.getHours() - 7) * 2 + (startDate.getMinutes() >= 30 ? 1 : 0));
                        let endSlot = Math.min(34, (endDate.getHours() - 7) * 2 + (endDate.getMinutes() >= 30 ? 1 : 0));
                        let duration = Math.max(1, endSlot - startSlot);

                        let descText = e.description || '';
                        let priority = 'medium';
                        let colorIdx = 0;
                        try {
                            if (descText.startsWith('{')) {
                                let descObj = JSON.parse(descText);
                                descText = descObj.desc || '';
                                priority = descObj.priority || 'medium';
                                colorIdx = descObj.color !== undefined ? descObj.color : 0;
                            }
                        } catch (err) {}

                        this.events.push({
                            id: e.id,
                            title: e.title,
                            description: descText,
                            dayIdx: dayIdx,
                            startSlot: startSlot,
                            duration: duration,
                            priority: priority,
                            colorIdx: colorIdx,
                            startDate: startDate,
                            endDate: endDate
                        });
                        this.dialogOpen = false;
                    }
                })
                .catch(err => console.error("Error creating event:", err));
            }
        },

        deleteEvent() {
            if (confirm('Are you sure you want to delete this personal event?')) {
                let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                let payload = new URLSearchParams();
                payload.append('eventId', this.formId);
                payload.append('__RequestVerificationToken', token);
                
                fetch('?handler=DeleteEvent', {
                    method: 'POST',
                    body: payload
                })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        this.events = this.events.filter(e => e.id !== this.formId);
                        this.dialogOpen = false;
                    }
                })
                .catch(err => console.error("Error deleting event:", err));
            }
        },

        duplicateEvent() {
            if (!this.formId) return;
            let targetDayIdx = (this.formDayIdx + 1) % 7;
            let { startTime, endTime } = this.slotsToISOTimes(targetDayIdx, this.formStartSlot, this.formDuration);
            
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let payload = new URLSearchParams();
            
            let descJson = JSON.stringify({
                desc: this.formDesc,
                priority: this.formPriority,
                color: this.formColor
            });
            
            payload.append('title', this.formTitle);
            payload.append('description', descJson);
            payload.append('startTime', startTime);
            payload.append('endTime', endTime);
            payload.append('__RequestVerificationToken', token);

            fetch('?handler=CreateEvent', {
                method: 'POST',
                body: payload
            })
            .then(res => res.json())
            .then(data => {
                if (data.success) {
                    let e = data.eventItem;
                    let startDate = new Date(e.startTime);
                    let endDate = new Date(e.endTime);
                    let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                    let startSlot = Math.max(0, (startDate.getHours() - 7) * 2 + (startDate.getMinutes() >= 30 ? 1 : 0));
                    let endSlot = Math.min(34, (endDate.getHours() - 7) * 2 + (endDate.getMinutes() >= 30 ? 1 : 0));
                    let duration = Math.max(1, endSlot - startSlot);

                    let descText = e.description || '';
                    let priority = 'medium';
                    let colorIdx = 0;
                    try {
                        if (descText.startsWith('{')) {
                            let descObj = JSON.parse(descText);
                            descText = descObj.desc || '';
                            priority = descObj.priority || 'medium';
                            colorIdx = descObj.color !== undefined ? descObj.color : 0;
                        }
                    } catch (err) {}

                    this.events.push({
                        id: e.id,
                        title: e.title,
                        description: descText,
                        dayIdx: dayIdx,
                        startSlot: startSlot,
                        duration: duration,
                        priority: priority,
                        colorIdx: colorIdx,
                        startDate: startDate,
                        endDate: endDate
                    });
                    this.dialogOpen = false;
                }
            })
            .catch(err => console.error("Error duplicating event:", err));
        }
    };
}
