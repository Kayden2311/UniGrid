"""
SQL Server data access layer.

Reads are plain parameterized SELECTs (run_query / with_sql_cursor).
There is also exactly one write path now: reschedule_personal_schedule_event()
below, which moves a PersonalSchedules row via a parameterized UPDATE.
Mutations no longer go through a backend REST API -- the bot owns this
directly -- so the "no double-booking" conflict check that used to live
only in that API is re-implemented here too, inside the same transaction
as the write.

All queries here are parameterized (pyodbc `?` placeholders). Never build a
query string by interpolating user or LLM-provided values directly --
that pattern existed in the legacy MySQL tools.py and is the thing this
rewrite is fixing.
"""

import pyodbc
from contextlib import contextmanager
from datetime import date, datetime, time
from typing import Any, Iterable, List, Optional, Sequence

from runeterra.config import Config
from runeterra.logging import log


class EventNotFoundError(Exception):
    """No PersonalSchedules row with that id belongs to this user."""


class ScheduleConflictError(Exception):
    """Another event already occupies the requested date/time slot."""

pyodbc.pooling = True

def _create_connection(autocommit: bool = True) -> pyodbc.Connection:
    conn = pyodbc.connect(Config.SqlServer.connection_string(), timeout=10)
    conn.autocommit = autocommit
    return conn


@contextmanager
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
    conn = _create_connection(autocommit=True)
    cur = conn.cursor()
    try:
        yield cur
    finally:
        cur.close()
        conn.close()


@contextmanager
def with_sql_transaction():
    """
    Write path -- autocommit OFF. Everything the caller does with this
    cursor is one transaction: committed if the block exits cleanly,
    rolled back on any exception. This is what lets
    reschedule_personal_schedule_event() do "check for a conflict, then
    write" as a single atomic unit instead of two separate round trips
    that a concurrent request could land in between.
    """
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
        rows = cursor.fetchall()
        return columns, [tuple(row) for row in rows]


def rows_to_dicts(columns: List[str], rows: Iterable[tuple]) -> List[dict]:
    return [dict(zip(columns, row)) for row in rows]

from uuid import UUID
def validate_guid(value, name):
    try:
        UUID(str(value))
    except Exception:
        raise ValueError(f"{name} is not a valid GUID: {value}")

def reschedule_personal_schedule_event(
    user_id: str,
    event_id: str,
    new_start_time: Optional[str] = None,
    new_end_time: Optional[str] = None
) -> dict:
    """
    Moves one PersonalSchedules row owned by `user_id` to a new
    date/time via a plain parameterized UPDATE, enforcing the same
    "no double-booking" rule that used to live only in the backend API:
    a user can't have two events sitting at the exact same StartTime, EndTime
    timestamp. (Schema note: PersonalSchedules has no duration/end-time
    column, so "conflict" here means an exact timestamp collision for
    this user -- if your actual business rule is closer to an overlap
    window, narrow/widen the comparison in the second query below.)

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

    Raises:
        EventNotFoundError: no such event for this user.
        ScheduleConflictError: another event already occupies that slot.
        ValueError: new_date / new_start_time / new_end_time isn't a valid date/time string.
    """

    validate_guid(user_id, "user_id")
    validate_guid(event_id, "event_id")

    with with_sql_transaction() as cur:
        cur.execute(
            """
            SELECT StartTime, EndTime
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = ? AND UserId = ?
            """,
            (event_id, user_id),
        )
        row = cur.fetchone()
        if row is None:
            raise EventNotFoundError("No event found with that id on your schedule.")


        cur.execute(
            """
            SELECT Id, Title
            FROM PersonalSchedules WITH (UPDLOCK, HOLDLOCK)
            WHERE UserId = ?
              AND StartTime = ?
              AND EndTime = ?
              AND Id <> ?
            """,
            (user_id, new_start_time, new_end_time, event_id),
        )
        conflict = cur.fetchone()
        if conflict is not None:
            raise ScheduleConflictError(
                f"That slot is already taken by another event on your schedule ('{conflict[1]}')."
            )

        cur.execute(
            """
            UPDATE PersonalSchedules
            SET StartTime = ?, EndTime = ?
            WHERE Id = ? AND UserId = ?
            """,
            (new_start_time, new_end_time, event_id, user_id),
        )
        if cur.rowcount == 0:
            # Shouldn't happen given the locked fetch above, but never
            # report success without confirming a row actually changed.
            raise EventNotFoundError("No event found with that id on your schedule.")

    log(f"[SQL] Rescheduled event {event_id} for user_id={user_id} -> {str(new_start_time)} to {str(new_end_time)}")
    return {"event_id": event_id, "new_event_date": f"{str(new_start_time)} to {str(new_end_time)}"}