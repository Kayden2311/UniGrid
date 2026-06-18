"""
Week-boundary utilities.

Date math is intentionally kept OUT of the LLM's hands. The model is
unreliable at arithmetic and prone to off-by-one errors on week boundaries,
so every tool that needs "this week" / "next week" / "week of X" resolves
the actual calendar dates here in Python, then hands the LLM a clean
(start_date, end_date) pair to use in its query / response.

Week convention: ISO week, Monday start, Sunday end.
  e.g. 2026-06-17 (Wednesday) -> week_start=2026-06-15 (Mon), week_end=2026-06-21 (Sun)
"""

from dataclasses import dataclass
from datetime import date, datetime, timedelta
from typing import Optional


@dataclass(frozen=True)
class WeekRange:
    start: date  # Monday
    end: date    # Sunday
    label: str   # human-readable, e.g. "Jun 15 - Jun 21, 2026"
    offset: int  # 0 = this week, -1 = last week, +1 = next week, etc.

    def to_dict(self) -> dict:
        return {
            "start_date": self.start.isoformat(),
            "end_date": self.end.isoformat(),
            "label": self.label,
            "offset": self.offset,
        }


def _format_label(start: date, end: date) -> str:
    if start.month == end.month:
        return f"{start.strftime('%b %d')} - {end.strftime('%d, %Y')}"
    return f"{start.strftime('%b %d')} - {end.strftime('%b %d, %Y')}"


def get_week_range(reference_date: Optional[date] = None, week_offset: int = 0) -> WeekRange:
    """
    Returns the Monday-Sunday week range containing `reference_date`,
    shifted by `week_offset` weeks (e.g. -1 for last week, +1 for next week).

    If reference_date is None, uses today's date (server time).
    """
    ref = reference_date or date.today()
    monday_this_week = ref - timedelta(days=ref.weekday())  # weekday(): Mon=0
    start = monday_this_week + timedelta(weeks=week_offset)
    end = start + timedelta(days=6)
    return WeekRange(start=start, end=end, label=_format_label(start, end), offset=week_offset)


def get_current_week_label() -> str:
    """Convenience helper for system-prompt injection: 'Today is ...; this week is ...'."""
    today = date.today()
    wr = get_week_range(today)
    return (
        f"Today's date is {today.isoformat()} ({today.strftime('%A')}). "
        f"The current week runs from {wr.start.isoformat()} (Monday) "
        f"to {wr.end.isoformat()} (Sunday)."
    )


def resolve_relative_week(phrase: str, reference_date: Optional[date] = None) -> WeekRange:
    """
    Maps a small set of relative phrases to a WeekRange. This is a safety net
    for tool args -- the LLM should normally pass an explicit offset, but if
    it passes a phrase instead this keeps behavior predictable.
    """
    phrase = phrase.strip().lower()
    mapping = {
        "this week": 0,
        "current week": 0,
        "next week": 1,
        "last week": -1,
        "previous week": -1,
    }
    offset = mapping.get(phrase, 0)
    return get_week_range(reference_date, week_offset=offset)


if __name__ == "__main__":
    # Sanity check against the example in the spec: today=2026-06-17 -> 2026-06-15..2026-06-21
    test_ref = datetime(2026, 6, 17).date()
    wr = get_week_range(test_ref)
    assert wr.start == date(2026, 6, 15), wr.start
    assert wr.end == date(2026, 6, 21), wr.end
    print("OK:", wr.to_dict())