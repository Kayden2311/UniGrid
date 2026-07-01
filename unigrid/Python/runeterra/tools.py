"""
Agent tools for the scheduling assistant.

Design rules baked into every tool below:
1. Every read is scoped to the calling user's own PersonalSchedules rows.
   `user_id` is injected by the agent layer from the authenticated session
   -- it is NOT an LLM-controllable argument on any tool. This is what
   keeps "show me my schedule" from ever becoming "show me anyone's
   schedule."
2. Reads use parameterized SQL Server queries only. No string interpolation
   of values into SQL text.
3. The only mutation is `reschedule_event`. It now writes directly to
   SQL Server with a parameterized UPDATE (see
   db.reschedule_personal_schedule_event), wrapped in a transaction that
   also re-implements the "no double-booking" conflict check -- that
   check used to live only in the backend API, and now that mutations
   no longer go through the backend, this tool is the one enforcing it.
4. Week/date math is resolved in Python (week_utils.py) before the LLM
   ever sees it, so "this week" / "next week" always map to the correct
   calendar dates.
"""

from typing import Any, List, Optional
from datetime import datetime, timezone, timedelta

from langchain.tools import tool
from langchain_core.messages import ToolMessage
from langchain_core.messages.tool import ToolCall
from langchain_core.tools import BaseTool
from runeterra.context import current_user_id

from runeterra.db import (
    run_query,
    rows_to_dicts,
    reschedule_personal_schedule_event,
    EventNotFoundError,
    ScheduleConflictError,
)
from runeterra.week_utils import get_week_range, resolve_relative_week
from runeterra.logging import log, log_panel

USER_TZ = timezone(timedelta(hours=7))

def _ensure_dt(value) -> datetime:
    """
    Normalize DB value to an aware datetime in UTC.
    If value is a string, parse with fromisoformat.
    If naive datetime, assume it's UTC.
    """
    if isinstance(value, str):
        # fromisoformat supports both date and datetime with optional tz
        dt = datetime.fromisoformat(value)
    elif isinstance(value, datetime):
        dt = value
    else:
        raise ValueError("Unsupported datetime value type")
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def _format_for_user(dt_value) -> str:
    """
    Convert a UTC datetime value to user's timezone (UTC+7) and format
    to a short human-friendly string.
    """
    try:
        dt = _ensure_dt(dt_value)
    except Exception:
        return str(dt_value)
    local = dt.astimezone(USER_TZ)
    # Example: Thu, Jun 18 2026 03:30 (UTC+7)
    return local.strftime("%a, %b %d %Y %H:%M") + " (UTC+7)"

def get_available_tools() -> List[BaseTool]:
    return [
        get_schedule_for_week,
        get_event_details,
        reschedule_event,
    ]


def call_tool(tool_call: ToolCall) -> Any:
    """
    Dispatches a tool call, always injecting the trusted `user_id` from the
    authenticated session into the tool's actual implementation -- not from
    tool_call["args"], even if the model hallucinated a user_id field there.
    """
    tools_by_name = {t.name: t for t in get_available_tools()}
    
    tool_obj = tools_by_name[tool_call["name"]]
    args = dict(tool_call["args"])
    args.pop("user_id", None)  # defensively strip; user_id is never LLM-supplied
    response = tool_obj.invoke(args)
    return ToolMessage(content=str(response), tool_call_id=tool_call["id"])


@tool(parse_docstring=False)
def get_schedule_for_week(week_offset: int = 0) -> str:
    """
    Retrieves the current user's personal schedule events for a given week.

    Args:
        week_offset: Which week relative to the current one. 0 = this week,
            -1 = last week, 1 = next week, 2 = two weeks from now, etc.

    Returns:
        Returns schedule events.

    IMPORTANT:
        Each event contains an EVENT_ID.
        EVENT_ID is an internal identifier that must be used when calling
        get_event_details or reschedule_event.

        Never invent an EVENT_ID.
        Always use the EVENT_ID returned by this tool.

        Do not display EVENT_ID values in final user-facing responses.

    Note: This function intentionally omits event IDs from human-facing
    schedule lines to avoid leaking identifiers in UI responses. Times
    are presented in UTC+7.
    """

    user_id = current_user_id.get()

    if not user_id:
        raise ValueError(
            "Authenticated user ID was not injected."
        )

    week = get_week_range(week_offset=week_offset)

    sql = """
        SELECT "Id", "Title", "StartTime", "EndTime", "TimeZone", "TaskId"
        FROM "PersonalSchedules"
        WHERE "UserId" = %s
          AND "StartTime" >= %s
          AND "EndTime" < %s::date + INTERVAL '1 day'
        ORDER BY "StartTime" ASC
    """
    columns, rows = run_query(sql, (user_id, week.start.isoformat(), week.end.isoformat()))
    events = rows_to_dicts(columns, rows)

    if not events:
        return (
            f"No events found for the week of {week.label} "
            f"({week.start.isoformat()} to {week.end.isoformat()})."
        )

    lines = [f"Week: {week.label} ({week.start.isoformat()} to {week.end.isoformat()})"]
    for e in events:
        start_str = _format_for_user(e.get("StartTime"))
        tz = e.get("TimeZone", "n/a")
        title = e.get("Title", "Untitled")
        event_id = str(e.get("Id"))

        lines.append(
            f"[EVENT_ID={event_id}] "
            f"{title} — {start_str} (TZ: {tz})"
        )
    return "\n".join(lines)


@tool(parse_docstring=False)
def get_event_details(event_id: str) -> str:
    """
    Retrieves full details for a single personal schedule event, only if
    it belongs to the current user.

    Args:
        event_id: The id of the PersonalSchedules event to look up.

    Returns:
        The event's details, or a message if it doesn't exist / doesn't
        belong to the current user.
    """
    sql = """
        SELECT "Id", "Title", "StartTime", "EndTime", "TimeZone", "TaskId"
        FROM "PersonalSchedules"
        WHERE "Id" = %s AND "UserId" = %s
    """

    user_id = current_user_id.get()

    if not user_id:
        raise ValueError(
            "Authenticated user ID was not injected."
        )

    print("event_id =", repr(event_id))
    print("user_id =", repr(user_id))

    columns, rows = run_query(sql, (event_id, user_id))
    events = rows_to_dicts(columns, rows)

    if not events:
        return "No event found with that id on your schedule."

    e = events[0]
    details = (
        f"Event: {e.get('Title')}\n"
        f"When: {_format_for_user(e.get('StartTime'))} to {_format_for_user(e.get('EndTime'))}\n"
        f"Time zone: {e.get('TimeZone', 'n/a')}"
    )
    if e.get("TaskId"):
        details += f"\nSynced from task: {e['TaskId']}"
    return details


@tool(parse_docstring=False)
def reschedule_event(
    event_id: str,
    new_start_time: Optional[str] = None,
    new_end_time: Optional[str] = None
) -> str:
    """
    Reschedules one of the current user's personal schedule events to a
    new date (and optionally time) with a direct, parameterized SQL
    Server UPDATE. Only works on events owned by the current user --
    ownership is enforced in the UPDATE's WHERE clause -- and the move is
    rejected if another of the user's events already sits at that exact
    date/time.

        Args:
        event_id: The id of the PersonalSchedules event to move. Get this
            from get_schedule_for_week or get_event_details first -- never guess
            an id, and never ask the user for one; ids are internal and must
            come from those tools' results, not from the user.
        new_start_time: has the format of yyyy-MM-dd HH:MM:SS.MSMSMSM
        new_end_time: has the format of yyyy-MM-dd HH:MM:SS.MSMSMSM and must be larger than new_start_time

    Returns:
        Confirmation of the new schedule, or an explanation of why the
        reschedule could not be completed (e.g. conflict, not found).
    """
    user_id = current_user_id.get()

    if not user_id:
        raise ValueError(
            "Authenticated user ID was not injected."
        )
    
    try:
        reschedule_personal_schedule_event(
            user_id=user_id,
            event_id=event_id,
            new_start_time=new_start_time,
            new_end_time=new_end_time
        )
        # Confirmation message: do not expose internal id in user-facing text
        return (
            f"Done — event has been moved from {new_start_time} to {new_end_time}."
        )
    except EventNotFoundError as e:
        return str(e)
    except ScheduleConflictError as e:
        log(f"[red]Reschedule conflict: {e}[/red]")
        return f"Could not reschedule that event: {e}"
    except ValueError:
        return "That date or time wasn't in a valid format -- please use YYYY-MM-DD and HH:MM (24h)."