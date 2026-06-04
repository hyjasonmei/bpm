#!/bin/sh
exec claude --channels plugin:telegram@claude-plugins-official --dangerously-skip-permissions "$@"
