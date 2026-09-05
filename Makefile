DOTNET_VERSION := 10.0.x
SOLUTION := HR.sln

.PHONY: build lint test scan ci eval seed up down

build:
	dotnet restore $(SOLUTION)
	dotnet build $(SOLUTION) --configuration Release -warnaserror

lint:
	dotnet format $(SOLUTION) --verify-no-changes --no-restore --verbosity diagnostic

test:
	dotnet test $(SOLUTION) --configuration Release --no-restore

scan:
	bash scripts/scan-secrets.sh
	dotnet list $(SOLUTION) package --vulnerable --include-transitive

ci: build lint test scan

eval:
	dotnet run --project HR.Evaluation --configuration Release

seed:
	dotnet run --project HR.Tools --configuration Release -- ingest-corpus

up:
	docker compose up --build

down:
	docker compose down