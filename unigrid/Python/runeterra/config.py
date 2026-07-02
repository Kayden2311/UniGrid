import os
from dataclasses import dataclass
from enum import Enum


class ModelProvider(str, Enum):
    OLLAMA = "ollama"
    GROQ = "groq"
    GPT = "gpt"


@dataclass
class ModelConfig:
    name: str
    temperature: float
    provider: ModelProvider


LLAMA_3 = ModelConfig("llama3", 0.0, ModelProvider.OLLAMA)
GPT_OSS = ModelConfig("openai/gpt-oss-120b", 0.0, ModelProvider.GROQ)


class Config:
    MODEL = GPT_OSS
    OLLAMA_CONTEXT_WINDOW = 2048

    class SqlServer:
        """
        SQL Server connection settings -- used for both reads (schedule
        lookups) and the bot's one write path: rescheduling a
        PersonalSchedules row via a parameterized UPDATE inside a
        transaction (see db.reschedule_personal_schedule_event). There's
        still no general-purpose write/execute-arbitrary-SQL path; that
        single UPDATE is gated by UserId and an in-transaction conflict
        check, same as before.

        NOTE: the SQL login below needs SELECT on Tasks and
        PersonalSchedules, plus UPDATE and INSERT on PersonalSchedules,
        because schedule mutations write here directly instead of going
        through a backend API.
        """
        HOST = os.getenv("SQLSERVER_HOST", "localhost")
        PORT = int(os.getenv("SQLSERVER_PORT", 1433))
        USER = os.getenv("SQLSERVER_USER", "sa")
        PASSWORD = os.getenv("SQLSERVER_PASSWORD", "123")
        DATABASE = os.getenv("SQLSERVER_DATABASE", "UniGridDb")
        DRIVER = os.getenv("SQLSERVER_DRIVER", "ODBC Driver 18 for SQL Server")
        # Set to "yes" only for local/dev boxes without a real TLS cert.
        TRUST_SERVER_CERTIFICATE = os.getenv("SQLSERVER_TRUST_CERT", "yes")

        @classmethod
        def connection_string(cls) -> str:
            return (
                f"DRIVER={{{cls.DRIVER}}};"
                f"SERVER={cls.HOST};"
                f"DATABASE={cls.DATABASE};"
                f"UID={cls.USER};"
                f"PWD={cls.PASSWORD};"
                f"TrustServerCertificate={cls.TRUST_SERVER_CERTIFICATE};"
            )
