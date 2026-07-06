"""
<<<<<<< HEAD
SQL Server data access layer.

Reads are plain parameterized SELECTs (run_query / with_sql_cursor).
Write paths are narrow helpers below: reschedule_personal_schedule_event()
moves a PersonalSchedules row, and schedule_unscheduled_task() creates a
PersonalSchedules row from one assigned Tasks row. Mutations no longer go
through a backend REST API -- the bot owns this directly -- so ownership,
unscheduled-task, and no-overlap checks are enforced here inside the same
transaction as the write.

All queries here are parameterized (pyodbc `?` placeholders). Never build a
query string by interpolating user or LLM-provided values directly --
that pattern existed in the legacy MySQL tools.py and is the thing this
rewrite is fixing.
"""

import pyodbc
from contextlib import contextmanager
from datetime import date, datetime, time, timezone
from typing import Any, Iterable, List, Optional, Sequence
=======
PostgreSQL (Supabase) data access layer.

Reads are plain parameterized SELECTs (run_query / with_pg_cursor).
The one write path is reschedule_personal_schedule_event(), which moves
a PersonalSchedules row via a parameterized UPDATE inside a transaction,
with a conflict check using SELECT ... FOR UPDATE to prevent double-booking.

All queries use %s placeholders (psycopg2 style). Never interpolate
user or LLM-provided values directly into SQL strings.
"""

import psycopg2
import psycopg2.extras
from contextlib import contextmanager
from typing import Any, Iterable, List, Optional, Sequence
from uuid import UUID
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49

from runeterra.config import Config
from runeterra.logging import log


class EventNotFoundError(Exception):
    """No PersonalSchedules row with that id belongs to this user."""


class ScheduleConflictError(Exception):
    """Another event already occupies the requested date/time slot."""


<<<<<<< HEAD
class TaskNotFoundError(Exception):
    """No active unscheduled Tasks row belongs to this user."""


class TaskAlreadyScheduledError(Exception):
    """The requested task already has an active PersonalSchedules row."""


class TaskDueDateViolationError(Exception):
    """The requested schedule is after the task's due date."""

pyodbc.pooling = True

def _create_connection(autocommit: bool = True) -> pyodbc.Connection:
    conn = pyodbc.connect(Config.SqlServer.connection_string(), timeout=10)
=======
def _create_connection(autocommit: bool = True) -> psycopg2.extensions.connection:
    conn = psycopg2.connect(**Config.Postgres.connection_kwargs())
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
    conn.autocommit = autocommit
    return conn


@contextmanager
<<<<<<< HEAD
def with_sql_cursor():
    """
    Read path -- autocommit connection, since plain SELECTs never need a
    transaction.

    pyodbc doesn't ship a built-in pool the way mysql-connector did.
    For a low/medium-traffic internal bot, a fresh connection per call
    (with the driver's own connection reuse) is simple and safe. If this
    becomes a bottleneck, swap in a proper pool (e.g. `pyodbc` + a small
    queue, or sqlalchemy's pool with pyodbc as the DBAPI).
    """
=======
def with_pg_cursor():
    """Read path -- autocommit connection for plain SELECTs."""
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
    conn = _create_connection(autocommit=True)
    cur = conn.cursor()
    try:
        yield cur
    finally:
        cur.close()
        conn.close()


@contextmanager
<<<<<<< HEAD
def with_sql_transaction():
    """
    Write path -- autocommit OFF. Everything the caller does with this
    cursor is one transaction: committed if the block exits cleanly,
    rolled back on any exception. This is what lets
    reschedule_personal_schedule_event() do "check for a conflict, then
    write" as a single atomic unit instead of two separate round trips
    that a concurrent request could land in between.
    """
=======
def with_pg_transaction():
    """Write path -- autocommit OFF, commit on success, rollback on error."""
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
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


<<<<<<< HEAD
def run_query(sql: str, params: Sequence[Any] = ()) -> List[tuple]:
    """
    Executes a parameterized SELECT and returns rows as a list of tuples.

    `sql` must use `?` placeholders; `params` are bound positionally by
    pyodbc. Never f-string user-controlled values into `sql`.
    """
    log(f"[SQL] {sql} | params={params}")
    with with_sql_cursor() as cursor:
        cursor.execute(sql, params)
        columns = [col[0] for col in cursor.description] if cursor.description else []
=======
# Keep old names as aliases so tools.py needs no changes
with_sql_cursor = with_pg_cursor
with_sql_transaction = with_pg_transaction


def run_query(sql: str, params: Sequence[Any] = ()):
    """
    Executes a parameterized SELECT and returns (columns, rows).
    Uses %s placeholders (psycopg2 style).
    """
    log(f"[SQL] {sql} | params={params}")
    with with_pg_cursor() as cursor:
        cursor.execute(sql, params)
        columns = [desc[0] for desc in cursor.description] if cursor.description else []
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
        rows = cursor.fetchall()
        return columns, [tuple(row) for row in rows]


def rows_to_dicts(columns: List[str], rows: Iterable[tuple]) -> List[dict]:
    return [dict(zip(columns, row)) for row in rows]


<<<<<<< HEAD
def _parse_datetime2(value: str, name: str) -> datetime:
    try:
        dt = datetime.fromisoformat(str(value).strip())
    except Exception:
        raise ValueError(f"{name} is not a valid datetime.")

    if dt.tzinfo is not None:
        dt = dt.astimezone(timezone.utc).replace(tzinfo=None)
    return dt


from uuid import UUID
=======
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
def validate_guid(value, name):
    try:
        UUID(str(value))
    except Exception:
        raise ValueError(f"{name} is not a valid GUID: {value}")

<<<<<<< HEAD
=======

>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
def reschedule_personal_schedule_event(
    user_id: str,
    event_id: str,
    new_start_time: Optional[str] = None,
<<<<<<< HEAD
    new_end_time: Optional[str] = None
) -> dict:
    """
    Moves one PersonalSchedules row owned by `user_id` to a new
    date/time via a plain parameterized UPDATE, enforcing the same
    "no double-booking" rule that used to live only in the backend API:
    a user can't have two enabled events whose StartTime/EndTime windows
    overlap.

    Runs as a single transaction:
      1. Lock + fetch the target event (must belong to `user_id`).
      2. Resolve the new StartTime, EndTime
      3. Lock + check for any *other* event owned by this user already
         at that exact timestamp.
      4. UPDATE if clear.

    The WITH (UPDLOCK, HOLDLOCK) hints on both SELECTs hold row/range
    locks for the rest of the transaction, so two concurrent reschedule
    calls can't both pass the conflict check and then both write into
    the same slot. (For this to be fast rather than just correct, an
    index on PersonalSchedules(UserId, StartTime, EndTime) is recommended -- this
    lookup used to be the backend's problem, it's this query's problem
    now.)
=======
    new_end_time: Optional[str] = None,
) -> dict:
    """
    Moves one PersonalSchedules row owned by `user_id` to a new date/time.
    Runs as a single transaction:
      1. Lock + fetch the target event (must belong to user_id).
      2. Check for any other event at the same slot (FOR UPDATE lock).
      3. UPDATE if clear.
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49

    Raises:
        EventNotFoundError: no such event for this user.
        ScheduleConflictError: another event already occupies that slot.
<<<<<<< HEAD
        ValueError: new_date / new_start_time / new_end_time isn't a valid date/time string.
    """

    validate_guid(user_id, "user_id")
    validate_guid(event_id, "event_id")
    parsed_start = _parse_datetime2(new_start_time, "new_start_time")
    parsed_end = _parse_datetime2(new_end_time, "new_end_time")
    if parsed_end <= parsed_start:
        raise ValueError("new_end_time must be later than new_start_time.")

    with with_sql_transaction() as cur:
        cur.execute(
            """
            SELECT StartTime, EndTime
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = ? AND UserId = ? AND IsDisabled = 0
=======
        ValueError: invalid GUID or datetime string.
    """
    validate_guid(user_id, "user_id")
    validate_guid(event_id, "event_id")

    with with_pg_transaction() as cur:
        # Step 1: lock and fetch the target event
        cur.execute(
            """
            SELECT "StartTime", "EndTime"
            FROM "PersonalSchedules"
            WHERE "Id" = %s AND "UserId" = %s
            FOR UPDATE
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
            """,
            (event_id, user_id),
        )
        row = cur.fetchone()
        if row is None:
            raise EventNotFoundError("No event found with that id on your schedule.")

<<<<<<< HEAD

        cur.execute(
            """
            SELECT Id, Title
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE UserId = ?
              AND IsDisabled = 0
              AND StartTime < ?
              AND EndTime > ?
              AND Id <> ?
            """,
            (user_id, parsed_end, parsed_start, event_id),
=======
        # Step 2: conflict check
        cur.execute(
            """
            SELECT "Id", "Title"
            FROM "PersonalSchedules"
            WHERE "UserId" = %s
              AND "StartTime" = %s
              AND "EndTime" = %s
              AND "Id" <> %s
            FOR UPDATE
            """,
            (user_id, new_start_time, new_end_time, event_id),
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
        )
        conflict = cur.fetchone()
        if conflict is not None:
            raise ScheduleConflictError(
                f"That slot is already taken by another event on your schedule ('{conflict[1]}')."
            )

<<<<<<< HEAD
        cur.execute(
            """
            UPDATE PersonalSchedules
            SET StartTime = ?, EndTime = ?
            WHERE Id = ? AND UserId = ? AND IsDisabled = 0
            """,
            (parsed_start, parsed_end, event_id, user_id),
        )
        if cur.rowcount == 0:
            # Shouldn't happen given the locked fetch above, but never
            # report success without confirming a row actually changed.
            raise EventNotFoundError("No event found with that id on your schedule.")

    log(f"[SQL] Rescheduled event {event_id} for user_id={user_id} -> {str(parsed_start)} to {str(parsed_end)}")
    return {"event_id": event_id, "new_event_date": f"{str(parsed_start)} to {str(parsed_end)}"}


def schedule_unscheduled_task(
    user_id: str,
    task_id: str,
    start_time: str,
    end_time: str,
    time_zone: str = "UTC",
) -> dict:
    """
    Creates a PersonalSchedules row for one active, unscheduled Tasks row
    assigned to `user_id`.

    Constraint checks enforced here:
      1. `task_id` and `user_id` must be valid GUIDs.
      2. The task must exist, be enabled, and be assigned to this user.
      3. The task must not already have an enabled PersonalSchedules row.
      4. EndTime must be later than StartTime.
      5. The schedule must not end after the task's DueDate calendar day.
      6. The user cannot have another enabled event overlapping the slot.
      7. Inserted PersonalSchedules fields satisfy NOT NULL and FK columns.
    """

    validate_guid(user_id, "user_id")
    validate_guid(task_id, "task_id")

    parsed_start = _parse_datetime2(start_time, "start_time")
    parsed_end = _parse_datetime2(end_time, "end_time")
    if parsed_end <= parsed_start:
        raise ValueError("end_time must be later than start_time.")

    clean_time_zone = (time_zone or "UTC").strip()[:100] or "UTC"

    with with_sql_transaction() as cur:
        cur.execute(
            """
            SELECT Id, Title, Description, DueDate
            FROM Tasks WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = ?
              AND AssigneeId = ?
              AND IsDisabled = 0
            """,
            (task_id, user_id),
        )
        task = cur.fetchone()
        if task is None:
            raise TaskNotFoundError("No active unscheduled task found for your account.")

        due_date = task[3]
        if due_date is not None:
            due_dt = _parse_datetime2(due_date, "due_date")
            if parsed_start.date() > due_dt.date() or parsed_end.date() > due_dt.date():
                raise TaskDueDateViolationError(
                    "That task cannot be scheduled after its due date."
                )

        cur.execute(
            """
            SELECT Id
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE TaskId = ?
              AND IsDisabled = 0
            """,
            (task_id,),
        )
        if cur.fetchone() is not None:
            raise TaskAlreadyScheduledError("That task is already on a schedule.")

        cur.execute(
            """
            SELECT Id, Title
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE UserId = ?
              AND IsDisabled = 0
              AND StartTime < ?
              AND EndTime > ?
            """,
            (user_id, parsed_end, parsed_start),
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
            INSERT INTO PersonalSchedules
                (UserId, Title, Description, StartTime, EndTime, TaskId, TimeZone)
            OUTPUT INSERTED.Id
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (
                user_id,
                title,
                description,
                parsed_start,
                parsed_end,
                task_id,
                clean_time_zone,
            ),
        )
        schedule_id = cur.fetchone()[0]

    log(f"[SQL] Scheduled task {task_id} for user_id={user_id} -> {str(parsed_start)} to {str(parsed_end)}")
    return {
        "schedule_id": str(schedule_id),
        "task_id": task_id,
        "title": title,
        "start_time": str(parsed_start),
        "end_time": str(parsed_end),
        "time_zone": clean_time_zone,
        "due_date": str(due_date) if due_date is not None else None,
    }
=======
        # Step 3: update
        cur.execute(
            """
            UPDATE "PersonalSchedules"
            SET "StartTime" = %s, "EndTime" = %s
            WHERE "Id" = %s AND "UserId" = %s
            """,
            (new_start_time, new_end_time, event_id, user_id),
        )
        if cur.rowcount == 0:
            raise EventNotFoundError("No event found with that id on your schedule.")

    log(f"[SQL] Rescheduled event {event_id} for user_id={user_id} -> {new_start_time} to {new_end_time}")
    return {"event_id": event_id, "new_event_date": f"{new_start_time} to {new_end_time}"}
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
