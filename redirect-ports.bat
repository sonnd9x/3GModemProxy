#@echo off

FOR /L %%G IN (%1,1,%2) DO netsh interface portproxy set v4tov4 %%G %3 %%G