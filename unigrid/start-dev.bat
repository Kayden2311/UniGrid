@echo off

start cmd /k "cd /d M:\UniGrid\unigrid\Python && .venv\Scripts\activate && uvicorn runeterra.api:app --reload"

start cmd /k "cd /d M:\UniGrid\unigrid && dotnet run"