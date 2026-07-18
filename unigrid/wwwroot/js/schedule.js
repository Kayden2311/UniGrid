/* UniGrid Calendar Scheduling Component Engine */

function scheduleComponent() {
    return {
        weekOffset: 0,
        events: [],
        workspaceTasks: [],
        tasks: window.scheduleRawTasks || [],
        assistantRefreshHandler: null,
        isRefreshingFromAssistant: false,
        
        // Confirmation Modal and Pending states
        confirmDialogOpen: false,
        pendingChange: null,
        hoveredId: null,
        ignoreNextClick: false,
        
        dialogOpen: false,
        alertModalOpen: false,
        alertModalTitle: 'Scheduling Conflict',
        alertModalMessage: '',
        isEdit: false,
        isTaskForm: false,
        formWorkspaceName: '',
        formWorkspaceJoinCode: '',
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
        formDate: '',
        formStartTimeVal: '',
        formEndTimeVal: '',
        formColor: 0,
        formPriority: 'medium',
        formDayIdx: 0,
        formStartSlot: 0,
        formDuration: 2,
        formTimeZone: 'UTC',

        // Custom time picker state
        startHour: '09',
        startMinute: '00',
        startAmpm: 'AM',
        startTimeDropdownOpen: false,

        endHour: '10',
        endMinute: '00',
        endAmpm: 'AM',
        endTimeDropdownOpen: false,

        updateStartTimeFromCustom() {
            let h = parseInt(this.startHour);
            if (this.startAmpm === 'PM' && h < 12) h += 12;
            if (this.startAmpm === 'AM' && h === 12) h = 0;
            this.formStartTimeVal = `${h.toString().padStart(2, '0')}:${this.startMinute}`;
        },

        updateEndTimeFromCustom() {
            let h = parseInt(this.endHour);
            if (this.endAmpm === 'PM' && h < 12) h += 12;
            if (this.endAmpm === 'AM' && h === 12) h = 0;
            this.formEndTimeVal = `${h.toString().padStart(2, '0')}:${this.endMinute}`;
        },

        parseTimesToCustom() {
            if (this.formStartTimeVal) {
                let parts = this.formStartTimeVal.split(':');
                let h24 = parseInt(parts[0]);
                this.startMinute = parts[1];
                this.startAmpm = h24 >= 12 ? 'PM' : 'AM';
                let h12 = h24 % 12;
                if (h12 === 0) h12 = 12;
                this.startHour = h12.toString().padStart(2, '0');
            }
            if (this.formEndTimeVal) {
                let parts = this.formEndTimeVal.split(':');
                let h24 = parseInt(parts[0]);
                this.endMinute = parts[1];
                this.endAmpm = h24 >= 12 ? 'PM' : 'AM';
                let h12 = h24 % 12;
                if (h12 === 0) h12 = 12;
                this.endHour = h12.toString().padStart(2, '0');
            }
        },

        formatLocalDate(date) {
            let y = date.getFullYear();
            let m = (date.getMonth() + 1).toString().padStart(2, '0');
            let d = date.getDate().toString().padStart(2, '0');
            return `${y}-${m}-${d}`;
        },

        formatLocalTime(date) {
            let h = date.getHours().toString().padStart(2, '0');
            let m = date.getMinutes().toString().padStart(2, '0');
            return `${h}:${m}`;
        },

        formatDateTimeAMPM(date) {
            let months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
            let m = months[date.getMonth()];
            let d = date.getDate();
            let y = date.getFullYear();
            let h = date.getHours();
            let min = date.getMinutes().toString().padStart(2, '0');
            let suffix = h >= 12 ? 'PM' : 'AM';
            let displayHour = h % 12;
            if (displayHour === 0) displayHour = 12;
            return `${m} ${d}, ${y}, ${displayHour.toString().padStart(2, '0')}:${min} ${suffix}`;
        },

        formatDeadlineDate(date) {
            let days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
            let dayName = days[date.getDay()];
            let h = date.getHours();
            let m = date.getMinutes().toString().padStart(2, '0');
            let suffix = h >= 12 ? 'PM' : 'AM';
            let displayHour = h % 12;
            if (displayHour === 0) displayHour = 12;
            return `${dayName}, ${displayHour.toString().padStart(2, '0')}:${m} ${suffix}`;
        },

        formatExactTimeRange(startD, endD) {
            if (!startD || !endD) return '';
            let start = new Date(startD);
            let end = new Date(endD);
            
            let sh = start.getHours();
            let sm = start.getMinutes().toString().padStart(2, '0');
            let sSuffix = sh >= 12 ? 'PM' : 'AM';
            let sHour = sh % 12 === 0 ? 12 : sh % 12;

            let eh = end.getHours();
            let em = end.getMinutes().toString().padStart(2, '0');
            let eSuffix = eh >= 12 ? 'PM' : 'AM';
            let eHour = eh % 12 === 0 ? 12 : eh % 12;

            let sStr = sHour.toString().padStart(2, '0') + ':' + sm;
            let eStr = eHour.toString().padStart(2, '0') + ':' + em;
            if (sSuffix === eSuffix) {
                return `${sStr} - ${eStr} ${eSuffix}`;
            } else {
                return `${sStr} ${sSuffix} - ${eStr} ${eSuffix}`;
            }
        },

        getFormStartEndISOTimes() {
            let startD = new Date(`${this.formDate}T${this.formStartTimeVal}`);
            let endD = new Date(`${this.formDate}T${this.formEndTimeVal}`);
            return {
                startTime: startD.toISOString(),
                endTime: endD.toISOString()
            };
        },

        init() {
            if (!this.assistantRefreshHandler) {
                this.assistantRefreshHandler = () => this.refreshScheduleSnapshot();
                window.addEventListener('unigrid:schedule-changed', this.assistantRefreshHandler);
            }

            // Load serialized C# Razor Page models
            let rawEvents = window.scheduleRawEvents || [];
            let rawTasks = window.scheduleRawTasks || [];
            
            let allMapped = rawEvents.map(e => {
                let startDate = new Date(e.startTime);
                let endDate = new Date(e.endTime);
                let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

                let isTask = !!e.taskId;
                let workspaceName = '';
                let workspaceJoinCode = '';
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

                if (isTask) {
                    let t = rawTasks.find(x => x.id === e.taskId);
                    if (t) {
                        workspaceName = t.workspaceName || 'Workspace';
                        workspaceJoinCode = t.workspaceJoinCode || '';
                        priority = t.priority || 'medium';
                        colorIdx = t.priority === 'high' ? 3 : (t.priority === 'medium' ? 2 : 4);
                    }
                }

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
                    endDate: endDate,
                    isTask: isTask,
                    taskId: e.taskId,
                    workspaceName: workspaceName,
                    workspaceJoinCode: workspaceJoinCode,
                    timeZone: e.timeZone || 'UTC'
                };
            });

            this.events = allMapped.filter(e => !e.isTask);
            this.workspaceTasks = allMapped.filter(e => e.isTask);
        },

        async refreshScheduleSnapshot() {
            if (this.isRefreshingFromAssistant) return;
            this.isRefreshingFromAssistant = true;
            try {
                const response = await fetch('/api/events/snapshot', {
                    headers: { 'Accept': 'application/json' },
                    cache: 'no-store'
                });
                if (!response.ok) throw new Error(`HTTP ${response.status}`);

                const snapshot = await response.json();
                window.scheduleRawEvents = Array.isArray(snapshot.events) ? snapshot.events : [];
                window.scheduleRawTasks = Array.isArray(snapshot.tasks) ? snapshot.tasks : [];
                this.tasks = window.scheduleRawTasks;
                this.init();
            } catch (error) {
                console.error('Unable to refresh schedule after assistant update:', error);
            } finally {
                this.isRefreshingFromAssistant = false;
            }
        },

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

        isEventInDay(hoveredId, dayIdx) {
            if (!hoveredId) return false;
            let ev = this.events.find(x => x.id === hoveredId) || this.workspaceTasks.find(x => x.id === hoveredId);
            return ev && ev.dayIdx === dayIdx;
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

        getPersonalEventsForDay(dayIdx) {
            let targetDate = this.weekDates[dayIdx];
            if (!targetDate) return [];
            let dayEvents = this.events.filter(e => {
                let d = new Date(e.startDate);
                return d.getFullYear() === targetDate.getFullYear() &&
                       d.getMonth() === targetDate.getMonth() &&
                       d.getDate() === targetDate.getDate();
            });

            // Get all tasks for this day to perform cross-lane overlap checks
            let dayTasks = this.workspaceTasks.filter(t => {
                let d = new Date(t.startDate);
                return d.getFullYear() === targetDate.getFullYear() &&
                       d.getMonth() === targetDate.getMonth() &&
                       d.getDate() === targetDate.getDate();
            });

            // Sort chronologically
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

                // Check if ANY event in this connected component overlaps with ANY workspace task on this day
                let hasCrossOverlap = g.events.some(ev => {
                    return dayTasks.some(task => {
                        return ev.startSlot < task.startSlot + task.duration && 
                               task.startSlot < ev.startSlot + ev.duration;
                    });
                });

                let laneStart = 0;
                let laneWidth = 100;
                if (hasCrossOverlap) {
                    laneStart = 0;
                    laneWidth = 48; // Confine to left half
                }

                let numCols = groupCols.length;
                for (let ev of g.events) {
                    ev.left = laneStart + (ev.colIdx / numCols) * laneWidth;
                    ev.width = laneWidth / numCols;
                }
            }

            return dayEvents;
        },

        getTasksForDay(dayIdx) {
            let targetDate = this.weekDates[dayIdx];
            if (!targetDate) return [];
            let dayTasks = this.workspaceTasks.filter(e => {
                let d = new Date(e.startDate);
                return d.getFullYear() === targetDate.getFullYear() &&
                       d.getMonth() === targetDate.getMonth() &&
                       d.getDate() === targetDate.getDate();
            });

            // Get all personal events for this day to perform cross-lane overlap checks
            let dayEvents = this.events.filter(e => {
                let d = new Date(e.startDate);
                return d.getFullYear() === targetDate.getFullYear() &&
                       d.getMonth() === targetDate.getMonth() &&
                       d.getDate() === targetDate.getDate();
            });

            // Sort chronologically
            dayTasks.sort((a, b) => a.startSlot - b.startSlot || b.duration - a.duration);

            // Group overlapping tasks in the day
            let groups = [];
            for (let ev of dayTasks) {
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

                // Check if ANY task in this connected component overlaps with ANY personal event on this day
                let hasCrossOverlap = g.events.some(task => {
                    return dayEvents.some(ev => {
                        return task.startSlot < ev.startSlot + ev.duration && 
                               ev.startSlot < task.startSlot + task.duration;
                    });
                });

                let laneStart = 0;
                let laneWidth = 100;
                if (hasCrossOverlap) {
                    laneStart = 52; // Confine to right half (leaving 4% central gutter)
                    laneWidth = 48;
                }

                let numCols = groupCols.length;
                for (let ev of g.events) {
                    ev.left = laneStart + (ev.colIdx / numCols) * laneWidth;
                    ev.width = laneWidth / numCols;
                }
            }

            return dayTasks;
        },

        get weeklyDeadlines() {
            let dates = this.weekDates;
            let startOfWeek = new Date(dates[0]);
            startOfWeek.setHours(0, 0, 0, 0);
            let endOfWeek = new Date(dates[6]);
            endOfWeek.setHours(23, 59, 59, 999);
            
            let scheduledTaskIds = this.workspaceTasks.map(t => t.taskId);
            
            return this.tasks.filter(t => {
                if (!t.dueDate) return false;
                if (scheduledTaskIds.includes(t.id)) return false;
                let dDate = new Date(t.dueDate);
                return dDate >= startOfWeek && dDate <= endOfWeek;
            }).map(t => {
                let dDate = new Date(t.dueDate);
                return {
                    id: t.id,
                    title: t.title,
                    description: t.description || '',
                    workspaceName: t.workspaceName,
                    workspaceJoinCode: t.workspaceJoinCode || '',
                    formattedDate: this.formatDeadlineDate(dDate),
                    priority: t.priority,
                    isTask: true
                };
            });
        },

        get unscheduledTasks() {
            let scheduledTaskIds = this.workspaceTasks.map(t => t.taskId);
            let dates = this.weekDates;
            let startOfWeek = new Date(dates[0]);
            startOfWeek.setHours(0, 0, 0, 0);
            let endOfWeek = new Date(dates[6]);
            endOfWeek.setHours(23, 59, 59, 999);

            return this.tasks.filter(t => {
                if (scheduledTaskIds.includes(t.id)) return false;
                
                // If it has a due date in the current week, it goes to weeklyDeadlines instead
                if (t.dueDate) {
                    let dDate = new Date(t.dueDate);
                    if (dDate >= startOfWeek && dDate <= endOfWeek) return false;
                }
                return true;
            }).map(t => {
                let dDate = t.dueDate ? new Date(t.dueDate) : null;
                return {
                    id: t.id,
                    title: t.title,
                    description: t.description || '',
                    workspaceName: t.workspaceName,
                    workspaceJoinCode: t.workspaceJoinCode || '',
                    formattedDate: dDate ? this.formatDeadlineDate(dDate) : "Backlog",
                    priority: t.priority,
                    isTask: true
                };
            });
        },

        slotToTime(slot) {
            let totalMinutes = slot * 30;
            let h = Math.floor(totalMinutes / 60);
            let m = Math.floor(totalMinutes % 60).toString().padStart(2, '0');
            let suffix = h >= 12 ? 'PM' : 'AM';
            let displayHour = h % 12;
            if (displayHour === 0) displayHour = 12;
            return displayHour.toString().padStart(2, '0') + ':' + m + ' ' + suffix;
        },

        formatTimeRange(startSlot, duration) {
            let endSlot = startSlot + duration;
            let sTotal = startSlot * 30;
            let sh = Math.floor(sTotal / 60);
            let sm = Math.floor(sTotal % 60).toString().padStart(2, '0');
            let sSuffix = sh >= 12 ? 'PM' : 'AM';
            let sHour = sh % 12 === 0 ? 12 : sh % 12;

            let eTotal = endSlot * 30;
            let eh = Math.floor(eTotal / 60);
            let em = Math.floor(eTotal % 60).toString().padStart(2, '0');
            let eSuffix = eh >= 12 ? 'PM' : 'AM';
            let eHour = eh % 12 === 0 ? 12 : eh % 12;

            let sStr = sHour.toString().padStart(2, '0') + ':' + sm;
            let eStr = eHour.toString().padStart(2, '0') + ':' + em;
            if (sSuffix === eSuffix) {
                return `${sStr} - ${eStr} ${eSuffix}`;
            } else {
                return `${sStr} ${sSuffix} - ${eStr} ${eSuffix}`;
            }
        },

        slotToTimeStr(slot) {
            let totalMinutes = slot * 30;
            let h = Math.floor(totalMinutes / 60);
            let m = Math.floor(totalMinutes % 60).toString().padStart(2, '0');
            return `${h.toString().padStart(2, '0')}:${m}`;
        },
        getDisplayHour(slot) {
            let totalMinutes = slot * 30;
            let h = Math.floor(totalMinutes / 60) % 12;
            if (h === 0) h = 12;
            return h.toString().padStart(2, '0');
        },
        getDisplayMinute(slot) {
            let totalMinutes = slot * 30;
            return Math.floor(totalMinutes % 60).toString().padStart(2, '0');
        },
        isAM(slot) {
            let totalMinutes = slot * 30;
            let h = Math.floor(totalMinutes / 60);
            return h < 12;
        },
        incrementHour() {
            let newStart = Math.min(46, this.formStartSlot + 2);
            this.formStartSlot = newStart;
        },
        decrementHour() {
            let newStart = Math.max(0, this.formStartSlot - 2);
            this.formStartSlot = newStart;
        },
        incrementMinute() {
            let newStart = Math.min(47, this.formStartSlot + 1);
            this.formStartSlot = newStart;
        },
        decrementMinute() {
            let newStart = Math.max(0, this.formStartSlot - 1);
            this.formStartSlot = newStart;
        },
        setAM() {
            let h = Math.floor((this.formStartSlot * 30) / 60);
            if (h >= 12) {
                this.formStartSlot = Math.max(0, this.formStartSlot - 24);
            }
        },
        setPM() {
            let h = 7 + Math.floor(this.formStartSlot / 2);
            if (h < 12) {
                this.formStartSlot = Math.min(33, this.formStartSlot + 24);
            }
        },

        slotsToISOTimes(dayIdx, startSlot, duration) {
            let date = this.weekDates[dayIdx];
            let sh = Math.floor(startSlot / 2);
            let sm = Math.round((startSlot % 2) * 30);
            
            let startDate = new Date(date);
            startDate.setHours(sh, sm, 0, 0);
            
            let endDate = new Date(startDate.getTime() + duration * 30 * 60000);
            
            return {
                startTime: startDate.toISOString(),
                endTime: endDate.toISOString()
            };
        },

        getSlotDetails(startDate, endDate) {
            let startSlot = Math.max(0, startDate.getHours() * 2 + (startDate.getMinutes() / 30));
            let duration = (endDate.getTime() - startDate.getTime()) / (30 * 60000);
            if (startSlot + duration > 48) {
                duration = 48 - startSlot;
            }
            duration = Math.max(0.5, duration);
            return { startSlot, duration };
        },

        showAlert(title, message) {
            this.alertModalTitle = title;
            this.alertModalMessage = message;
            this.alertModalOpen = true;
        },

        openAdd(dayIdx, slotIdx, durationSlotCount = 2) {
            this.isEdit = false;
            this.isTaskForm = false;
            this.formWorkspaceName = '';
            this.formId = '';
            this.formTitle = '';
            this.formDesc = '';
            this.formPriority = 'medium';
            this.formColor = 0;
            this.formDayIdx = dayIdx;
            this.formStartSlot = slotIdx;
            this.formDuration = durationSlotCount;
            this.formTimeZone = 'UTC';
            
            let { startTime, endTime } = this.slotsToISOTimes(dayIdx, slotIdx, durationSlotCount);
            this.formStartTime = startTime;
            this.formEndTime = endTime;

            // Populate local date and times
            let date = this.weekDates[dayIdx];
            this.formDate = this.formatLocalDate(date);
            
            let sh = Math.floor(slotIdx / 2);
            let sm = slotIdx % 2 === 0 ? 0 : 30;
            this.formStartTimeVal = `${sh.toString().padStart(2, '0')}:${sm.toString().padStart(2, '0')}`;
            
            let eh = Math.floor((slotIdx + durationSlotCount) / 2);
            let em = (slotIdx + durationSlotCount) % 2 === 0 ? 0 : 30;
            this.formEndTimeVal = `${eh.toString().padStart(2, '0')}:${em.toString().padStart(2, '0')}`;

            this.parseTimesToCustom();

            this.dialogOpen = true;
        },

        openEdit(event) {
            if (this.ignoreNextClick) {
                this.ignoreNextClick = false;
                return;
            }
            this.isEdit = true;
            this.isTaskForm = !!event.isTask;
            this.formWorkspaceName = event.workspaceName || '';
            this.formWorkspaceJoinCode = event.workspaceJoinCode || '';
            this.formId = event.id;
            this.formTitle = event.title;
            this.formDesc = event.description;
            this.formPriority = event.priority;
            this.formColor = event.colorIdx;
            this.formTimeZone = event.timeZone || 'UTC';
            
            if (event.dayIdx !== null && event.dayIdx !== undefined) {
                this.formDayIdx = event.dayIdx;
                this.formStartSlot = event.startSlot;
                this.formDuration = event.duration || 2;
                
                let { startTime, endTime } = this.slotsToISOTimes(event.dayIdx, event.startSlot, event.duration || 2);
                this.formStartTime = startTime;
                this.formEndTime = endTime;

                let startD = new Date(event.startDate);
                let endD = new Date(event.endDate);
                this.formDate = this.formatLocalDate(startD);
                this.formStartTimeVal = this.formatLocalTime(startD);
                this.formEndTimeVal = this.formatLocalTime(endD);
            } else {
                // If it is unscheduled (clicked from the sidebar)
                if (event.dueDate) {
                    let d = new Date(event.dueDate);
                    let foundDayIdx = -1;
                    for (let i = 0; i < 7; i++) {
                        if (this.weekDates[i].toDateString() === d.toDateString()) {
                            foundDayIdx = i;
                            break;
                        }
                    }
                    if (foundDayIdx === -1) {
                        foundDayIdx = d.getDay() === 0 ? 6 : d.getDay() - 1;
                    }
                    this.formDayIdx = foundDayIdx;
                    
                    let hours = d.getHours();
                    let minutes = d.getMinutes();
                    if ((hours === 23 && minutes === 59) || (hours === 0 && minutes === 0)) {
                        this.formStartSlot = 18; // 9:00 AM (slot 18 in 24h system)
                    } else {
                        this.formStartSlot = Math.max(0, hours * 2 + (minutes >= 30 ? 1 : 0));
                    }

                    this.formDate = this.formatLocalDate(d);
                    this.formStartTimeVal = "09:00";
                    this.formEndTimeVal = "10:00";
                } else {
                    this.formDayIdx = 0; // Monday
                    this.formStartSlot = 18; // 9:00 AM (slot 18 in 24h system)

                    let d = this.weekDates[0];
                    this.formDate = this.formatLocalDate(d);
                    this.formStartTimeVal = "09:00";
                    this.formEndTimeVal = "10:00";
                }
                this.formDuration = 2;
                this.formStartTime = '';
                this.formEndTime = '';
            }
            this.parseTimesToCustom();
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
                
                let relX = evMouse.clientX - rectContainer.left;
                let relY = evMouse.clientY - rectContainer.top;
                
                let dayWidth = (rectContainer.width - 80) / 7;
                let day = 0;
                if (relX >= 80) {
                    day = Math.floor((relX - 80) / dayWidth);
                }
                day = Math.max(0, Math.min(day, 6));
                
                let slotIdx = Math.floor(relY / 36);
                slotIdx = Math.max(0, Math.min(slotIdx, 47));
                
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

        handleCellMouseEnter(dayIdx, slotIdx) {},

        async handleMouseUp() {},

        handleMouseLeave() {},

        handleDeadlineDragStart(ev, dl) {
            this.draggedTask = dl;
            ev.dataTransfer.effectAllowed = 'move';
            ev.dataTransfer.setData('text/plain', dl.id);
        },

        handleDeadlineDrop(ev, dayIdx, slotIdx) {
            ev.preventDefault();
            let dl = this.draggedTask;
            if (!dl) {
                let id = ev.dataTransfer.getData('text/plain');
                if (id) {
                    dl = this.weeklyDeadlines.find(d => d.id === id) || this.unscheduledTasks.find(d => d.id === id);
                }
            }
            if (!dl) return;

            let rawTask = this.tasks.find(x => x.id === dl.id);
            if (rawTask && rawTask.dueDate) {
                let { endTime } = this.slotsToISOTimes(dayIdx, slotIdx, 2);
                let dueD = new Date(rawTask.dueDate);
                let endD = new Date(endTime);
                if (endD > dueD) {
                    let formatted = this.formatDateTimeAMPM(dueD);
                    this.showAlert("Scheduling Conflict", `You cannot schedule this task past its due date (<strong>${formatted}</strong>).`);
                    this.draggedTask = null;
                    return;
                }
            }

            let originalDayIdx = null;
            let originalStartSlot = null;
            if (rawTask && rawTask.dueDate) {
                let dDate = new Date(rawTask.dueDate);
                originalDayIdx = dDate.getDay() === 0 ? 6 : dDate.getDay() - 1;
                let hours = dDate.getHours();
                let minutes = dDate.getMinutes();
                if (hours === 0 && minutes === 0) {
                    hours = 9;
                    minutes = 0;
                }
                originalStartSlot = Math.max(0, hours * 2 + (minutes >= 30 ? 1 : 0));
            }

            this.pendingChange = {
                isTask: true,
                taskId: dl.id,
                title: dl.title,
                originalDayIdx: originalDayIdx,
                originalStartSlot: originalStartSlot,
                originalDuration: 2,
                newDayIdx: dayIdx,
                newStartSlot: slotIdx,
                newDuration: 2
            };

            this.confirmDialogOpen = true;
            this.draggedTask = null;
        },

        startMoveTask(e, ev, dayIdx) {
            let gridContainer = document.getElementById('grid-container');
            if (!gridContainer) return;
            
            this.dragStartX = e.clientX;
            this.dragStartY = e.clientY;
            this.dragTranslateX = 0;
            this.dragTranslateY = 0;
            this.dragMoved = false;

            let sContainer = gridContainer.closest('.overflow-auto');
            this.startScrollTop = sContainer ? sContainer.scrollTop : 0;
            this.startScrollLeft = sContainer ? sContainer.scrollLeft : 0;

            let cardEl = document.getElementById((ev.isTask ? 'task-' : 'ev-') + ev.id);
            let rectCard = cardEl.getBoundingClientRect();
            let offsetY = e.clientY - rectCard.top;
            let offsetSlot = Math.floor(offsetY / 36);

            this.mode = 'moving';
            this.movingTask = {
                taskId: ev.id,
                isTask: !!ev.isTask,
                offsetSlot: offsetSlot,
                sourceDayIdx: dayIdx,
                currentDayIdx: dayIdx,
                currentStartSlot: ev.startSlot
            };

            let onMove = (evMouse) => {
                if (this.mode !== 'moving' || !this.movingTask) return;

                let dx = evMouse.clientX - this.dragStartX;
                let dy = evMouse.clientY - this.dragStartY;

                let rectContainer = gridContainer.getBoundingClientRect();
                let sContainer = gridContainer.closest('.overflow-auto');
                let sTop = sContainer ? sContainer.scrollTop : 0;
                let sLeft = sContainer ? sContainer.scrollLeft : 0;

                let dsTop = sTop - this.startScrollTop;
                let dsLeft = sLeft - this.startScrollLeft;

                // Update physical offset for absolute free-pixel coordinate moving, adjusting for scroll delta
                this.dragTranslateX = dx + dsLeft;
                this.dragTranslateY = dy + dsTop;

                if (Math.abs(dx) > 4 || Math.abs(dy) > 4) {
                    this.dragMoved = true;
                }

                let relX = evMouse.clientX - rectContainer.left;
                let relY = evMouse.clientY - rectContainer.top;

                let dayWidth = (rectContainer.width - 80) / 7;
                let day = 0;
                if (relX >= 80) {
                    day = Math.floor((relX - 80) / dayWidth);
                }
                day = Math.max(0, Math.min(day, 6));

                let slotIdx = Math.floor(relY / 36);
                slotIdx = Math.max(0, Math.min(slotIdx, 47));

                let targetEv = this.movingTask.isTask
                    ? this.workspaceTasks.find(x => x.id === this.movingTask.taskId)
                    : this.events.find(x => x.id === this.movingTask.taskId);
                if (targetEv) {
                    let newStart = Math.max(0, Math.min(slotIdx - this.movingTask.offsetSlot, 48 - targetEv.duration));
                    this.movingTask.currentDayIdx = day;
                    this.movingTask.currentStartSlot = newStart;
                }
            };

            let onUp = () => {
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);

                if (this.mode === 'moving' && this.movingTask) {
                    let targetEv = this.movingTask.isTask
                        ? this.workspaceTasks.find(x => x.id === this.movingTask.taskId)
                        : this.events.find(x => x.id === this.movingTask.taskId);
                    if (targetEv && this.dragMoved) {
                        this.ignoreNextClick = true;
                        let originalDayIdx = targetEv.dayIdx;
                        let originalStartSlot = targetEv.startSlot;
                        let originalDuration = targetEv.duration;

                        // Visual snap immediately
                        targetEv.dayIdx = this.movingTask.currentDayIdx;
                        targetEv.startSlot = this.movingTask.currentStartSlot;

                        let { startTime, endTime } = this.slotsToISOTimes(targetEv.dayIdx, targetEv.startSlot, targetEv.duration);
                        targetEv.startDate = new Date(startTime);
                        targetEv.endDate = new Date(endTime);

                        // Capture pending change
                        this.pendingChange = {
                            isTask: !!targetEv.isTask,
                            taskId: targetEv.taskId || targetEv.id,
                            eventId: targetEv.id,
                            title: targetEv.title,
                            originalDayIdx: originalDayIdx,
                            originalStartSlot: originalStartSlot,
                            originalDuration: originalDuration,
                            newDayIdx: targetEv.dayIdx,
                            newStartSlot: targetEv.startSlot,
                            newDuration: targetEv.duration,
                            isResize: false
                        };
                        this.confirmDialogOpen = true;
                    } else if (targetEv && !this.dragMoved) {
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
                let slotDelta = Math.round(dy / 36);
                let targetEv = this.events.find(x => x.id === ev.id) || this.workspaceTasks.find(x => x.id === ev.id);
                if (!targetEv) return;
 
                if (edge === 'bottom') {
                    let newDuration = Math.max(1, this.resizingTask.origDuration + slotDelta);
                    targetEv.duration = Math.min(newDuration, 48 - targetEv.startSlot);
                } else {
                    let newStart = Math.max(0, this.resizingTask.origStartSlot + slotDelta);
                    let endSlot = this.resizingTask.origStartSlot + this.resizingTask.origDuration;
                    if (newStart >= endSlot) newStart = endSlot - 1;
                    targetEv.startSlot = newStart;
                    targetEv.duration = Math.max(1, endSlot - newStart);
                }
            };
 
            let onUp = () => {
                this.mode = 'idle';
                let targetEv = this.events.find(x => x.id === ev.id) || this.workspaceTasks.find(x => x.id === ev.id);
                if (targetEv) {
                    if (targetEv.startSlot !== this.resizingTask.origStartSlot || targetEv.duration !== this.resizingTask.origDuration) {
                        this.ignoreNextClick = true;
                        let { startTime, endTime } = this.slotsToISOTimes(targetEv.dayIdx, targetEv.startSlot, targetEv.duration);
                        targetEv.startDate = new Date(startTime);
                        targetEv.endDate = new Date(endTime);
 
                        this.pendingChange = {
                            isTask: !!targetEv.isTask,
                            taskId: targetEv.taskId || targetEv.id,
                            eventId: targetEv.id,
                            title: targetEv.title,
                            originalDayIdx: targetEv.dayIdx,
                            originalStartSlot: this.resizingTask.origStartSlot,
                            originalDuration: this.resizingTask.origDuration,
                            newDayIdx: targetEv.dayIdx,
                            newStartSlot: targetEv.startSlot,
                            newDuration: targetEv.duration,
                            isResize: true
                        };
                        this.confirmDialogOpen = true;
                    }
                }
                this.resizingTask = null;
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            };

            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        },

        async confirmPendingChange() {
            if (!this.pendingChange) return;
            let pc = this.pendingChange;
            if (pc.isTask) {
                let { startTime, endTime } = this.slotsToISOTimes(pc.newDayIdx, pc.newStartSlot, pc.newDuration);
                
                // Strict client-side due date check
                let rawTask = this.tasks.find(x => x.id === pc.taskId);
                if (rawTask && rawTask.dueDate) {
                    let dueD = new Date(rawTask.dueDate);
                    let endD = new Date(endTime);
                    if (endD > dueD) {
                        let formatted = this.formatDateTimeAMPM(dueD);
                        this.showAlert("Scheduling Conflict", `You cannot schedule this task past its due date (<strong>${formatted}</strong>).`);
                        this.cancelPendingChange();
                        return;
                    }
                }

                let existingWTask = this.workspaceTasks.find(x => x.taskId === pc.taskId);
                let timeZone = existingWTask ? existingWTask.timeZone : 'UTC';
                await this.updateTaskTimeInDb(pc.taskId, startTime, endTime, timeZone);
            } else {
                let targetEv = this.events.find(x => x.id === pc.eventId);
                if (targetEv) {
                    let { startTime, endTime } = this.slotsToISOTimes(pc.newDayIdx, pc.newStartSlot, pc.newDuration);
                    await this.updateEventTimeInDb(pc.eventId, startTime, endTime);
                }
            }
            this.confirmDialogOpen = false;
            this.pendingChange = null;
        },

        cancelPendingChange() {
            if (!this.pendingChange) return;
            let pc = this.pendingChange;
            if (pc.isTask) {
                let targetEv = this.workspaceTasks.find(x => x.taskId === pc.taskId);
                if (targetEv) {
                    targetEv.dayIdx = pc.originalDayIdx;
                    targetEv.startSlot = pc.originalStartSlot;
                    targetEv.duration = pc.originalDuration;
                    
                    if (pc.originalDayIdx !== null && pc.originalDayIdx !== undefined) {
                        let { startTime, endTime } = this.slotsToISOTimes(pc.originalDayIdx, pc.originalStartSlot, pc.originalDuration);
                        targetEv.startDate = new Date(startTime);
                        targetEv.endDate = new Date(endTime);
                    } else {
                        // It was originally unscheduled! Remove it from workspaceTasks
                        this.workspaceTasks = this.workspaceTasks.filter(x => x.taskId !== pc.taskId);
                    }
                }
                this.workspaceTasks = [...this.workspaceTasks]; // trigger reactivity!
            } else {
                let targetEv = this.events.find(x => x.id === pc.eventId);
                if (targetEv) {
                    targetEv.dayIdx = pc.originalDayIdx;
                    targetEv.startSlot = pc.originalStartSlot;
                    targetEv.duration = pc.originalDuration;
                    
                    let { startTime, endTime } = this.slotsToISOTimes(pc.originalDayIdx, pc.originalStartSlot, pc.originalDuration);
                    targetEv.startDate = new Date(startTime);
                    targetEv.endDate = new Date(endTime);
                }
                this.events = [...this.events]; // trigger reactivity!
            }
            this.confirmDialogOpen = false;
            this.pendingChange = null;
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
                let data = await response.json();
                if (data.success) {
                    let targetEv = this.events.find(x => x.id === eventId);
                    if (targetEv) {
                        let e = data.eventItem;
                        let startDate = new Date(e.startTime);
                        let endDate = new Date(e.endTime);
                        let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                        let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

                        targetEv.startDate = startDate;
                        targetEv.endDate = endDate;
                        targetEv.dayIdx = dayIdx;
                        targetEv.startSlot = startSlot;
                        targetEv.duration = duration;
                        this.events = [...this.events]; // trigger reactivity!
                    }
                } else {
                    this.showAlert("Scheduling Conflict", data.message || "Failed to update event time.");
                    this.init();
                }
            } catch (err) {
                console.error("Network error updating event times:", err);
                this.showAlert("Network Error", "Failed to update event time due to a network error.");
                this.init();
            }
        },

        async updateTaskTimeInDb(taskId, startTime, endTime, timeZone) {
            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let payload = new URLSearchParams();
            payload.append('taskId', taskId);
            payload.append('startTime', startTime);
            payload.append('endTime', endTime);
            payload.append('timeZone', timeZone || 'UTC');
            payload.append('__RequestVerificationToken', token);

            try {
                let response = await fetch('?handler=UpdateTaskTime', {
                    method: 'POST',
                    body: payload
                });
                let data = await response.json();
                if (data.success) {
                    let e = data.eventItem;
                    let startDate = new Date(e.startTime);
                    let endDate = new Date(e.endTime);
                    let dayIdx = startDate.getDay() === 0 ? 6 : startDate.getDay() - 1;
                    let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

                    let rawTask = this.tasks.find(x => x.id === taskId);
                    let wName = rawTask ? rawTask.workspaceName : 'Workspace';
                    let wJoinCode = rawTask ? rawTask.workspaceJoinCode : '';
                    let priority = rawTask ? rawTask.priority : 'medium';
                    let colorIdx = priority === 'high' ? 3 : (priority === 'medium' ? 2 : 4);

                    let existingWTaskIdx = this.workspaceTasks.findIndex(x => x.taskId === taskId);
                    let mappedWTask = {
                        id: e.id,
                        title: e.title,
                        description: e.description || '',
                        dayIdx: dayIdx,
                        startSlot: startSlot,
                        duration: duration,
                        priority: priority,
                        colorIdx: colorIdx,
                        startDate: startDate,
                        endDate: endDate,
                        isTask: true,
                        taskId: taskId,
                        workspaceName: wName,
                        workspaceJoinCode: wJoinCode,
                        timeZone: e.timeZone || 'UTC'
                    };

                    if (existingWTaskIdx !== -1) {
                        this.workspaceTasks[existingWTaskIdx] = mappedWTask;
                    } else {
                        this.workspaceTasks.push(mappedWTask);
                    }
                    this.workspaceTasks = [...this.workspaceTasks]; // trigger reactivity!
                } else {
                    this.showAlert("Scheduling Conflict", data.message || "Failed to schedule task.");
                    this.init();
                }
            } catch (err) {
                console.error("Network error updating task times:", err);
                this.showAlert("Network Error", "Failed to schedule task due to a network error.");
                this.init();
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
            
            let startD = new Date(`${this.formDate}T${this.formStartTimeVal}`);
            let endD = new Date(`${this.formDate}T${this.formEndTimeVal}`);
            if (endD <= startD) {
                this.showAlert("Invalid Time Range", "The end time must be after the start time.");
                return;
            }

            let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let payload = new URLSearchParams();
            
            let descJson = JSON.stringify({
                desc: this.formDesc,
                priority: this.formPriority,
                color: this.formColor
            });
            
            let startTime = startD.toISOString();
            let endTime = endD.toISOString();
            this.formStartTime = startTime;
            this.formEndTime = endTime;
            
            payload.append('title', this.formTitle);
            payload.append('description', descJson);
            payload.append('startTime', this.formStartTime);
            payload.append('endTime', this.formEndTime);
            payload.append('timeZone', this.formTimeZone);
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
                            let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

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
                                endDate: endDate,
                                timeZone: e.timeZone || 'UTC'
                            };
                            this.events = [...this.events]; // trigger reactivity!
                        }
                        this.dialogOpen = false;
                    } else {
                        this.showAlert("Scheduling Conflict", data.message || "Failed to edit event.");
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
                        let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

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
                            endDate: endDate,
                            timeZone: e.timeZone || 'UTC'
                        });
                        this.events = [...this.events]; // trigger reactivity!
                        this.dialogOpen = false;
                    } else {
                        this.showAlert("Scheduling Conflict", data.message || "Failed to create event.");
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
            
            let startD = new Date(`${this.formDate}T${this.formStartTimeVal}`);
            let endD = new Date(`${this.formDate}T${this.formEndTimeVal}`);
            
            // Increment date by 1 day
            startD.setDate(startD.getDate() + 1);
            endD.setDate(endD.getDate() + 1);
            
            let startTime = startD.toISOString();
            let endTime = endD.toISOString();
            
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
            payload.append('timeZone', this.formTimeZone);
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
                    let { startSlot, duration } = this.getSlotDetails(startDate, endDate);

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
                        endDate: endDate,
                        timeZone: e.timeZone || 'UTC'
                    });
                    this.events = [...this.events]; // trigger reactivity!
                    this.dialogOpen = false;
                } else {
                    this.showAlert("Scheduling Conflict", data.message || "Failed to duplicate event.");
                }
            })
            .catch(err => console.error("Error duplicating event:", err));
        },

        async saveTaskSchedule() {
            let startD = new Date(`${this.formDate}T${this.formStartTimeVal}`);
            let endD = new Date(`${this.formDate}T${this.formEndTimeVal}`);
            if (endD <= startD) {
                this.showAlert("Invalid Time Range", "The end time must be after the start time.");
                return;
            }
            
            let startTime = startD.toISOString();
            let endTime = endD.toISOString();
            
            // Strict client-side due date check
            let rawTask = this.tasks.find(x => x.id === this.formId);
            if (rawTask && rawTask.dueDate) {
                let dueD = new Date(rawTask.dueDate);
                if (endD > dueD) {
                    let formatted = this.formatDateTimeAMPM(dueD);
                    this.showAlert("Scheduling Conflict", `You cannot schedule this task past its due date (<strong>${formatted}</strong>).`);
                    return;
                }
            }

            await this.updateTaskTimeInDb(this.formId, startTime, endTime, this.formTimeZone);
            this.dialogOpen = false;
        },

        async unscheduleTask() {
            if (confirm('Are you sure you want to remove this task from your personal schedule?')) {
                let token = document.querySelector('input[name="__RequestVerificationToken"]').value;
                let payload = new URLSearchParams();
                payload.append('eventId', this.formId);
                payload.append('__RequestVerificationToken', token);
                
                try {
                    let response = await fetch('?handler=DeleteEvent', {
                        method: 'POST',
                        body: payload
                    });
                    let data = await response.json();
                    if (data.success) {
                        // Remove from workspaceTasks local list
                        this.workspaceTasks = this.workspaceTasks.filter(t => t.id !== this.formId);
                        this.dialogOpen = false;
                    } else {
                        this.showAlert("Error", data.message || "Failed to unschedule task.");
                    }
                } catch (err) {
                    console.error("Error unscheduling task:", err);
                    this.showAlert("Network Error", "Failed to unschedule task due to a network error.");
                }
            }
        }
    };
}
