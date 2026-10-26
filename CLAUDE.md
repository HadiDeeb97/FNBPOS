# Working on this repository

- **Every pull request raises `<Version>` in `Directory.Build.props`.** A new feature raises the middle number
  (1.1.0 → 1.2.0), fixes only raise the last one (1.1.0 → 1.1.1). The version shows in the program file's
  Properties → Details, which is how a shop checks that all its tills run the same version.
- Plan before building: describe a new feature and ask before changing code.
