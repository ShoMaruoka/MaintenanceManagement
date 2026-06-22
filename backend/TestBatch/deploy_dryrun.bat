@echo off
echo [DRY-RUN] deploy.bat - ログ出力のみ（実際のSQL適用は行いません）
echo.
echo Simulating: Connecting to STG SQL Server...
echo Simulating: Executing SQL files in ForNewCreation\Source\
echo   [OK] StoredProcedure\dbo.SampleProc.sql
echo   [OK] Function\dbo.FN_Sample.sql
echo Simulating: Verifying deployment...
echo All objects deployed successfully. (simulated)
echo.
echo [DRY-RUN] 完了 (exit code 0)
exit /b 0
