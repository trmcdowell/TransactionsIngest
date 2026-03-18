# Transactions Ingest

Transactions ingest is a .NET console application that uses SQLite to consolidate faked card transactions. The primary service is IngestionService, which pulls data from the json transactions feed as well as all existing transactions before putting both in maps. This allows for fast and easy lookups while detecting any differences between the feed and existing records. Every time a change is found, the transaction row is updated in the db and an transaction audit row is created that shows what field was changed and how. I also included transaction and audit statuses in order to handle the different transaction states.

## Commands

Build project:

```
dotnet build
```

Run project:

```
dotnet run --project TransactionsIngest
```

Run tests:

```
dotnet test
```
