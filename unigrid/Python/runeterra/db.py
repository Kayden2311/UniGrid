"""
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

from runeterra.config import Config
from runeterra.logging import log


class EventNotFoundError(Exception):
    """No PersonalSchedules row with that id belongs to this user."""


class ScheduleConflictError(Exception):
    """Another event already occupies the requested date/time slot."""


def _create_connection(autocommit: bool = True) -> psycopg2.extensions.connection:
    conn = psycopg2.connect(**Config.Postgres.connection_kwargs())
    conn.autocommit = autocommit
    return conn


@contextmanager
def with_pg_cursor():
    """Read path -- autocommit connection for plain SELECTs."""
    conn = _create_connection(autocommit=True)
    cur = conn.cursor()
    try:
        yield cur
    finally:
        cur.close()
        conn.close()


@contextmanager
def with_pg_transaction():
    """Write path -- autocommit OFF, commit on success, rollback on error."""
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
        rows = cursor.fetchall()
        return columns, [tuple(row) for row in rows]


def rows_to_dicts(columns: List[str], rows: Iterable[tuple]) -> List[dict]:
    return [dict(zip(columns, row)) for row in rows]


def validate_guid(value, name):
    try:
        UUID(str(value))
    except Exception:
        raise ValueError(f"{name} is not a valid GUID: {value}")


def reschedule_personal_schedule_event(
    user_id: str,
    event_id: str,
    new_start_time: Optional[str] = None,
    new_end_time: Optional[str] = None,
) -> dict:
    """
    Moves one PersonalSchedules row owned by `user_id` to a new date/time.
    Runs as a single transaction:
      1. Lock + fetch the target event (must belong to user_id).
      2. Check for any other event at the same slot (FOR UPDATE lock).
      3. UPDATE if clear.

    Raises:
        EventNotFoundError: no such event for this user.
        ScheduleConflictError: another event already occupies that slot.
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
            """,
            (event_id, user_id),
        )
        row = cur.fetchone()
        if row is None:
            raise EventNotFoundError("No event found with that id on your schedule.")

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
        )
        conflict = cur.fetchone()
        if conflict is not None:
            raise ScheduleConflictError(
                f"That slot is already taken by another event on your schedule ('{conflict[1]}')."
            )

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
