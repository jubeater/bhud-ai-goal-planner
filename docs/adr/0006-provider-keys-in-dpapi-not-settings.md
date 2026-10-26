# Store Provider keys encrypted with DPAPI, not in module Settings

Storing the Provider key as an ordinary module Setting would be the obvious choice, but Blish HUD writes all Settings to a plaintext `settings.json`, which players commonly share when asking for help. Keys are instead stored in the module's own folder, encrypted with Windows DPAPI for the current Windows user, one entry per Provider. They are never logged or shown to the AI.
