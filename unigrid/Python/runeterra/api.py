from fastapi import FastAPI, Request, Depends, HTTPException
from pydantic import BaseModel
from langchain_core.messages import HumanMessage, AIMessage
from dotenv import load_dotenv
from fastapi.middleware.cors import CORSMiddleware
from typing import Literal, List

from runeterra.config import Config
from runeterra.models import create_llm
from runeterra.agent import ask, create_history
from runeterra.tools import get_available_tools
from runeterra.logging import log_panel, green_border_style
from runeterra.context import current_user_id

load_dotenv()

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

llm = create_llm(Config.MODEL)
llm = llm.bind_tools(get_available_tools())


def get_current_user_id(request: Request) -> str:
    """
    Resolve the authenticated user's id.

    Priority:
     - If real platform auth is wired, implement JWT/session extraction here.
     - Otherwise for trusted backend-to-backend calls we accept header 'X-User-Id'.
    """
    # Check forwarded header first (backend-to-backend)
    header_user = request.headers.get("X-User-Id") or request.headers.get("x-user-id")
    if header_user:
        header_user = header_user.strip()
        if header_user:
            print(f"[runeterra] Received X-User-Id header: {header_user}")
            return header_user


    # No user id found — reject request explicitly
    raise HTTPException(status_code=401, detail="Missing authenticated user id (X-User-Id header or JWT).")


class Message(BaseModel):
    role: Literal["user", "assistant"]
    content: str


class ChatRequest(BaseModel):
    message: str
    history: List[Message]
    # NOTE: deliberately no user_id field here. User identity comes only
    # from get_current_user_id(request), never from the request payload.


@app.post("/chat")
def chat_endpoint(req: ChatRequest, user_id: str = Depends(get_current_user_id)):
    log_panel(
        title="Incoming API Request",
        content=f"user_id={user_id}\nMessage: {req.message}\nHistory length: {len(req.history)}",
        border_style=green_border_style,
    )

    chat_history = create_history()
    if req.history:
        for msg in req.history:
            if msg.role == "user":
                chat_history.append(HumanMessage(content=msg.content))
            elif msg.role == "assistant":
                chat_history.append(AIMessage(content=msg.content))

    current_user_id.set(user_id)

    reply = ask(
        req.message,
        chat_history,
        llm,
        user_id=user_id
    )

    log_panel(
        title="API Response",
        content=f"Reply: {reply}",
        border_style=green_border_style,
    )
    return {"reply": reply}


@app.get("/ping")
def ping():
    return {"status": "ok"}