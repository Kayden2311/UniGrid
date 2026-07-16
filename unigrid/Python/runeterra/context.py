from contextvars import ContextVar

current_user_id: ContextVar[str] = ContextVar(
    "current_user_id",
    default=""
)

schedule_data_changed: ContextVar[bool] = ContextVar(
    "schedule_data_changed",
    default=False,
)
