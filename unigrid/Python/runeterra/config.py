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

    class Postgres:
        """
        PostgreSQL (Supabase) connection settings.
        """
        HOST = os.getenv("PG_HOST", "localhost")
        PORT = int(os.getenv("PG_PORT", 5432))
        USER = os.getenv("PG_USER", "postgres")
        PASSWORD = os.getenv("PG_PASSWORD", "")
        DATABASE = os.getenv("PG_DATABASE", "postgres")

        @classmethod
        def connection_kwargs(cls) -> dict:
            return {
                "host": cls.HOST,
                "port": cls.PORT,
                "user": cls.USER,
                "password": cls.PASSWORD,
                "dbname": cls.DATABASE,
            }