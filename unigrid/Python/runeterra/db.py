"""PostgreSQL/Supabase data access for the scheduling chatbot.

All values are parameterized with psycopg2 ``%s`` placeholders. Reads use
short-lived autocommit connections. Schedule mutations run in transactions
and lock the relevant rows while ownership, due-date, and overlap checks are
performed.
"""

from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Any, Iterable, List, Optional, Sequence
from uuid import UUID

import psycopg2

from runeterra.config import Config
from runeterra.logging import log


class EventNotFoundError(Exception):
    """No active PersonalSchedules row belongs to this user."""


class ScheduleConflictError(Exception):
    """Another event overlaps the requested date/time slot."""


class TaskNotFoundError(Exception):
    """No active unscheduled Tasks row belongs to this user."""


class TaskAlreadyScheduledError(Exception):
    """The requested task already has an active PersonalSchedules row."""


class TaskDueDateViolationError(Exception):
    """The requested schedule is after the task's due date."""


def _create_connection(autocommit: bool = True):
    conn = psycopg2.connect(**Config.Postgres.connection_kwargs())
    conn.autocommit = autocommit
    return conn


@contextmanager
def with_pg_cursor():
    conn = _create_connection(autocommit=True)
    cur = conn.cursor()
    try:
        yield cur
    finally:
        cur.close()
        conn.close()


@contextmanager
def with_pg_transaction():
    conn = _create_connection(autocommit=False)
    cur = conn.cursor()
    try:
        yield cur
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        cur.close()
        conn.close()


# Compatibility aliases for existing imports.
with_sql_cursor = with_pg_cursor
with_sql_transaction = with_pg_transaction


def run_query(sql: str, params: Sequence[Any] = ()):
    """Execute a parameterized SELECT and return ``(columns, rows)``."""
    log(f"[SQL] {sql} | params={params}")
    with with_pg_cursor() as cursor:
        cursor.execute(sql, params)
        columns = [desc[0] for desc in cursor.description] if cursor.description else []
        rows = cursor.fetchall()
        return columns, [tuple(row) for row in rows]


def rows_to_dicts(columns: List[str], rows: Iterable[tuple]) -> List[dict]:
    return [dict(zip(columns, row)) for row in rows]


def _parse_datetime(value: Any, name: str) -> datetime:
    if isinstance(value, datetime):
        dt = value
    else:
        try:
            dt = datetime.fromisoformat(str(value).strip())
        except Exception as exc:
            raise ValueError(f"{name} is not a valid datetime.") from exc
    if dt.tzinfo is not None:
        dt = dt.astimezone(timezone.utc).replace(tzinfo=None)
    return dt


def validate_guid(value: Any, name: str) -> UUID:
    try:
        return UUID(str(value))
    except Exception as exc:
        raise ValueError(f"{name} is not a valid GUID: {value}") from exc


def reschedule_personal_schedule_event(
    user_id: str,
    event_id: str,
    new_start_time: Optional[str] = None,
    new_end_time: Optional[str] = None,
) -> dict:
    user_uuid = validate_guid(user_id, "user_id")
    event_uuid = validate_guid(event_id, "event_id")
    parsed_start = _parse_datetime(new_start_time, "new_start_time")
    parsed_end = _parse_datetime(new_end_time, "new_end_time")
    if parsed_end <= parsed_start:
        raise ValueError("new_end_time must be later than new_start_time.")

    with with_pg_transaction() as cur:
        # Serialize schedule mutations per user, including concurrent inserts
        # for which no row exists yet to lock.
        cur.execute("SELECT pg_advisory_xact_lock(hashtext(%s))", (str(user_uuid),))
        cur.execute(
            """
            SELECT "StartTime", "EndTime"
            FROM "PersonalSchedules"
            WHERE "Id" = %s AND "UserId" = %s AND "IsDisabled" = FALSE
            FOR UPDATE
            """,
            (str(event_uuid), str(user_uuid)),
        )
        if cur.fetchone() is None:
            raise EventNotFoundError("No event found with that id on your schedule.")

        cur.execute(
            """
            SELECT "Id", "Title"
            FROM "PersonalSchedules"
            WHERE "UserId" = %s
              AND "IsDisabled" = FALSE
              AND "StartTime" < %s
              AND "EndTime" > %s
              AND "Id" <> %s
            FOR UPDATE
            """,
            (str(user_uuid), parsed_end, parsed_start, str(event_uuid)),
        )
        conflict = cur.fetchone()
        if conflict is not None:
            raise ScheduleConflictError(
                f"That slot is already taken by another event on your schedule ('{conflict[1]}')."
            )

        cur.execute(
            """
            UPDATE "PersonalSchedules"
            SET "StartTime" = %s, "EndTime" = %s
            WHERE "Id" = %s AND "UserId" = %s AND "IsDisabled" = FALSE
            """,
            (parsed_start, parsed_end, str(event_uuid), str(user_uuid)),
        )
        if cur.rowcount == 0:
            raise EventNotFoundError("No event found with that id on your schedule.")

    log(f"[SQL] Rescheduled event {event_uuid} for user_id={user_uuid}")
    return {"event_id": str(event_uuid), "new_event_date": f"{parsed_start} to {parsed_end}"}


def schedule_unscheduled_task(
    user_id: str,
    task_id: str,
    start_time: str,
    end_time: str,
    time_zone: str = "UTC",
) -> dict:
    user_uuid = validate_guid(user_id, "user_id")
    task_uuid = validate_guid(task_id, "task_id")
    parsed_start = _parse_datetime(start_time, "start_time")
    parsed_end = _parse_datetime(end_time, "end_time")
    if parsed_end <= parsed_start:
        raise ValueError("end_time must be later than start_time.")
    clean_time_zone = (time_zone or "UTC").strip()[:100] or "UTC"

    with with_pg_transaction() as cur:
        cur.execute("SELECT pg_advisory_xact_lock(hashtext(%s))", (str(user_uuid),))
        cur.execute(
            """
            SELECT "Id", "Title", "Description", "DueDate"
            FROM "Tasks"
            WHERE "Id" = %s
              AND "AssigneeId" = %s
              AND "IsDisabled" = FALSE
            FOR UPDATE
            """,
            (str(task_uuid), str(user_uuid)),
        )
        task = cur.fetchone()
        if task is None:
            raise TaskNotFoundError("No active unscheduled task found for your account.")

        due_date = task[3]
        if due_date is not None:
            due_dt = _parse_datetime(due_date, "due_date")
            if parsed_start.date() > due_dt.date() or parsed_end.date() > due_dt.date():
                raise TaskDueDateViolationError("That task cannot be scheduled after its due date.")

        cur.execute(
            """
            SELECT "Id"
            FROM "PersonalSchedules"
            WHERE "TaskId" = %s AND "IsDisabled" = FALSE
            FOR UPDATE
            """,
            (str(task_uuid),),
        )
        if cur.fetchone() is not None:
            raise TaskAlreadyScheduledError("That task is already on a schedule.")

        cur.execute(
            """
            SELECT "Id", "Title"
            FROM "PersonalSchedules"
            WHERE "UserId" = %s
              AND "IsDisabled" = FALSE
              AND "StartTime" < %s
              AND "EndTime" > %s
            FOR UPDATE
            """,
            (str(user_uuid), parsed_end, parsed_start),
        )
        conflict = cur.fetchone()
        if conflict is not None:
            raise ScheduleConflictError(
                f"That slot overlaps another event on your schedule ('{conflict[1]}')."
            )

        title = str(task[1])[:256]
        description = task[2]
        cur.execute(
            """
            INSERT INTO "PersonalSchedules"
                ("UserId", "Title", "Description", "StartTime", "EndTime", "TaskId", "TimeZone")
            VALUES (%s, %s, %s, %s, %s, %s, %s)
            RETURNING "Id"
            """,
            (str(user_uuid), title, description, parsed_start, parsed_end, str(task_uuid), clean_time_zone),
        )
        schedule_id = cur.fetchone()[0]

    log(f"[SQL] Scheduled task {task_uuid} for user_id={user_uuid}")
    return {
        "schedule_id": str(schedule_id),
        "task_id": str(task_uuid),
        "title": title,
        "start_time": str(parsed_start),
        "end_time": str(parsed_end),
        "time_zone": clean_time_zone,
        "due_date": str(due_date) if due_date is not None else None,
    }
