import sys
import os
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

import pytest
from fastapi.testclient import TestClient
from unittest.mock import patch, MagicMock
from datetime import datetime
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage, ToolMessage
from runeterra.api import app, create_llm
from runeterra.agent import create_history, ask
from runeterra.tools import (
    get_available_tools,
    call_tool,
    execute_sql,
    recommend_mentors_by_topic,
    recommend_courses_by_topic,
    get_mentor_availability,
    get_top_rated_mentors,
    list_courses_for_mentor,
    get_mentor_tags,
    list_courses_and_tags_for_mentor,
    with_sql_cursor,
)

# Mock LLM and MySQL connector
@pytest.fixture
def mock_llm():
    llm = MagicMock()
    llm.bind_tools = MagicMock(return_value=llm)
    return llm

@pytest.fixture
def client():
    return TestClient(app)

@pytest.fixture
def mock_mysql_cursor():
    cursor = MagicMock()
    cursor.fetchall.return_value = []
    connection = MagicMock()
    connection.cursor.return_value = cursor
    return connection, cursor

# F1: Process chat requests
def test_chat_endpoint(client, mock_llm):
    with patch("runeterra.api.create_llm", return_value=mock_llm), \
            patch("runeterra.api.ask", return_value="Here are Java mentors: Alice, Bob"):
        response = client.post(
            "/chat",
            json={
                "message": "Recommend mentors for Java",
                "history": [
                    {"role": "user", "content": "I want to learn programming"},
                    {"role": "assistant", "content": "What specific topic?"}
                ]
            }
        )
        assert response.status_code == 200
        assert response.json() == {"reply": "Here are Java mentors: Alice, Bob"}

# F2: Respond to ping requests
def test_ping_endpoint(client):
    response = client.get("/ping")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}

# F3: Create initial system prompt
def test_create_history():
    history = create_history()
    assert len(history) == 1
    assert isinstance(history[0], SystemMessage)
    assert "You are Runeterra" in history[0].content
    assert f"Today is {datetime.now().strftime('%Y-%m-%d')}" in history[0].content

# F4: Handle full chat loop
def test_ask(mock_llm):
    history = [SystemMessage(content="You are Runeterra...")]
    query = "Find mentors for Cybersecurity"
    mock_llm.invoke.side_effect = [
        MagicMock(tool_calls=[{"name": "recommend_mentors_by_topic", "args": {"topic": "Cybersecurity", "limit": 3}, "id": "123"}]),
        MagicMock(content="Recommended mentors: Alice, Bob", tool_calls=[])
    ]
    with patch("runeterra.agent.call_tool", return_value=ToolMessage(content="('Alice', 'Cybersecurity', 4.5)", tool_call_id="123")):
        response = ask(query, history, mock_llm)
        assert response == "Recommended mentors: Alice, Bob"
        assert mock_llm.invoke.call_count == 2

def test_ask_max_iterations(mock_llm):
    history = [SystemMessage(content="You are Runeterra...")]
    query = "Find mentors"
    mock_llm.invoke.return_value = MagicMock()
    with patch("runeterra.tools.get_available_tools", return_value=[]):
        with pytest.raises(RuntimeError, match="Maximum number of iterations reaches"):
            ask(query, history, mock_llm, max_iterations=10)

# F5: Provide list of available tools
def test_get_available_tools():
    tools = get_available_tools()
    assert len(tools) == 6
    assert all(tool.name in [
        "execute_sql", "recommend_mentors_by_topic", "recommend_courses_by_topic",
        "get_mentor_availability", "get_top_rated_mentors", "list_courses_for_mentor"
    ] for tool in tools)

# F6: Execute tool based on AI request
def test_call_tool():
    tool_call = {
        "name": "recommend_mentors_by_topic",
        "args": {"topic": "Java", "limit": 3},
        "id": "123"
    }

    mock_tool = MagicMock()
    mock_tool.name = "recommend_mentors_by_topic"
    mock_tool.invoke.return_value = "('Alice', 'Java Expert', 4.5)"

    with patch("runeterra.tools.get_available_tools", return_value=[mock_tool]):
        result = call_tool(tool_call)

        assert isinstance(result, ToolMessage)
        assert result.content == "('Alice', 'Java Expert', 4.5)"
        assert result.tool_call_id == "123"
        mock_tool.invoke.assert_called_once_with({"topic": "Java", "limit": 3})

def test_call_tool_invalid():
    tool_call = {"name": "invalid_tool", "args": {}, "id": "123"}
    with patch("runeterra.tools.get_available_tools", return_value=[]):
        with pytest.raises(KeyError):
            call_tool(tool_call)

# F7: Execute raw SQL query
def test_execute_sql(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Alice",), ("Bob",)]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = execute_sql({"reasoning": "Test query", "sql_query": "SELECT full_name FROM users LIMIT 2"})
        assert result == "('Alice',)\n('Bob',)"
        cursor.execute.assert_called_with("SELECT full_name FROM users LIMIT 2")

def test_execute_sql_error(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.execute.side_effect = Exception("SQL error")
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection), \
            patch("runeterra.tools.log") as mock_log:
        result = execute_sql({"reasoning": "Test query", "sql_query": "SELECT * FROM nonexistent_table"})
        assert result == "Error running query: SQL error"
        mock_log.assert_called_with("[red]Error running query: SQL error[/red]")

# F8: Find mentors by topic
def test_recommend_mentors_by_topic(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Alice", "Java Expert", 4.5), ("Bob", "Full Stack", 4.0)]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = recommend_mentors_by_topic({"topic": "Java", "limit": 3})
        assert result == "('Alice', 'Java Expert', 4.5)\n('Bob', 'Full Stack', 4.0)"
        cursor.execute.assert_called()

# F9: Find open courses by topic
def test_recommend_courses_by_topic(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("AI Basics", "Introduction to AI"), ("Deep Learning", "Advanced AI")]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = recommend_courses_by_topic({"topic": "AI", "limit": 2})
        assert result == "('AI Basics', 'Introduction to AI')\n('Deep Learning', 'Advanced AI')"
        cursor.execute.assert_called()

# F10: Handle SQL execution with connection
def test_with_sql_cursor(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        with with_sql_cursor(readonly=True) as cur:
            cur.execute("SELECT 1")
        cursor.execute.assert_called_with("SELECT 1")
        connection.commit.assert_not_called()
        cursor.close.assert_called()
        connection.close.assert_called()

def test_with_sql_cursor_error(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.execute.side_effect = Exception("Connection error")
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        with pytest.raises(Exception, match="Connection error"):
            with with_sql_cursor(readonly=False) as cur:
                cur.execute("SELECT 1")
        connection.rollback.assert_called()
        cursor.close.assert_called()
        connection.close.assert_called()

# F11: Retrieve top-rated mentors
def test_get_top_rated_mentors(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Alice", "Java Expert", 4.8), ("Bob", "AI Specialist", 4.5)]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = get_top_rated_mentors({"limit": 5})
        assert result == "('Alice', 'Java Expert', 4.8)\n('Bob', 'AI Specialist', 4.5)"
        cursor.execute.assert_called()

# F12: Get mentor availability
def test_get_mentor_availability(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Alice", "Java Expert"), ("Bob", "AI Specialist")]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = get_mentor_availability({"day": "MONDAY"})
        assert result == "('Alice', 'Java Expert')\n('Bob', 'AI Specialist')"
        cursor.execute.assert_called()

# F13: Get mentor tags
def test_get_mentor_tags(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Java",), ("Spring",)]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = get_mentor_tags({"mentor_name": "Alice Smith"})
        assert result == "('Java',)\n('Spring',)"
        cursor.execute.assert_called()

# F14: List courses for mentor
def test_list_courses_for_mentor(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Java Basics", "Learn Java"), ("Spring Boot", "Build APIs")]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = list_courses_for_mentor({"mentor_name": "Bob Smith"})
        assert result == "('Java Basics', 'Learn Java')\n('Spring Boot', 'Build APIs')"
        cursor.execute.assert_called()

# F15: List courses and tags for mentor
def test_list_courses_and_tags_for_mentor(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.fetchall.return_value = [("Java Basics", "Java,Spring"), ("Advanced Java", "Java,Hibernate")]
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection):
        result = list_courses_and_tags_for_mentor({"mentor_name": "Alice Smith"})
        assert result == "('Java Basics', 'Java,Spring')\n('Advanced Java', 'Java,Hibernate')"
        cursor.execute.assert_called()

# F16: Simulate tool SQL execution failure
def test_sql_execution_failure(mock_mysql_cursor):
    connection, cursor = mock_mysql_cursor
    cursor.execute.side_effect = Exception("Table 'nonexistent_table' doesn't exist")
    with patch("runeterra.tools.mysql.connector.connect", return_value=connection), \
            patch("runeterra.tools.log") as mock_log:
        result = execute_sql({"reasoning": "Test query", "sql_query": "SELECT * FROM nonexistent_table"})
        assert result == "Error running query: Table 'nonexistent_table' doesn't exist"
        mock_log.assert_called_with("[red]Error running query: Table 'nonexistent_table' doesn't exist[/red]")
