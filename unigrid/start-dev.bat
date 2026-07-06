@echo off

<<<<<<< HEAD
start cmd /k "cd /d M:\UniGrid\unigrid\Python && .venv\Scripts\activate && uvicorn runeterra.api:app --reload"

start cmd /k "cd /d M:\UniGrid\unigrid && dotnet run"
=======
:: 1. Start the Python AI service
start cmd /k "cd /d "%~dp0Python" && .venv\Scripts\activate && uvicorn runeterra.api:app --reload"

:: 2. Start the ASP.NET Core backend
start cmd /k "cd /d "%~dp0" && dotnet run"

:: 3. Start the Vite React frontend
start cmd /k "cd /d "%~dp0..\..\unigrid_fe" && npm run dev"
>>>>>>> da388596d2baad13bd7723b6a42b2048d2b19e49
