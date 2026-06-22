@echo off
echo [DRY-RUN] git_merge.bat - ログ出力のみ（実際のgitマージは行いません）
echo.
echo Simulating: reading UpdateModule.txt
echo Simulating: git checkout dev_branch
echo Simulating: git merge origin/main
echo Merge made by the 'recursive' strategy. (simulated)
echo  StoredProcedure/dbo.SampleProc.sql | 12 ++++++------
echo  1 file changed, 6 insertions(+), 6 deletions(-)
echo.
echo [DRY-RUN] 完了 (exit code 0)
exit /b 0
