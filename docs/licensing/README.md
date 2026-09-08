# Licensing machine records

Human register: [../licensing-register.md](../licensing-register.md). Status note: [../../LICENSE-STATUS.md](../../LICENSE-STATUS.md).

`register.json` is the inventory the G0 validator loads. `candidates/*.template.json` are admission templates. A template is not an approved package, asset, or binary.

Admission (`approved` / `blocked` / `pending`) is the shippable conclusion. `technical` and `security` stay on separate fields. Public visibility is not a license grant.

HD-007, HD-014, HD-021, and HD-034 may fill a **new** candidate file from a template. They must not set `lock_allowed` or any `enters_*` flag unless `admission` is `approved`. Unknown or conflict stays `blocked`. HD-035 reverse-audits the real release inputs. AC02 remains `not_run`.
