@echo off

:: 1. Start the Python AI service
start cmd /k "cd /d "%~dp0Python" && .venv\Scripts\activate && uvicorn runeterra.api:app --reload"

:: 2. Start the ASP.NET Core backend
start cmd /k "cd /d "%~dp0" && dotnet run"

:: 3. Start the Vite React frontend
start cmd /k "cd /d "%~dp0..\..\unigrid_fe" && npm run dev"