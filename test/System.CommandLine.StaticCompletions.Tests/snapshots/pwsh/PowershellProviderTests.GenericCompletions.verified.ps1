﻿using namespace System.Management.Automation
using namespace System.Management.Automation.Language

Register-ArgumentCompleter -Native -CommandName 'mycommand' -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commandElements = $commandAst.CommandElements
    $command = @(
        'mycommand'
        for ($i = 1; $i -lt $commandElements.Count; $i++) {
        $element = $commandElements[$i]
            if ($element -isnot [StringConstantExpressionAst] -or
                $element.StringConstantType -ne [StringConstantType]::BareWord -or
                $element.Value.StartsWith('-') -or
                $element.Value -eq $wordToComplete) {
                break
            }
            $element.Value
        }) -join ';'

    $completions = @()
    switch ($command) {
        'mycommand' {
            $staticCompletions = @(
            )
            $completions += $staticCompletions
            break
        }
    }
    $completions.Where{ $_.CompletionText -like "$wordToComplete*" } | Sort-Object -Property ListItemText
}