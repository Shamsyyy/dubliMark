# \# DubliMark project instructions

# 

# Проект: C# / .NET 8 / WPF приложение для работы с DataMatrix / Честным знаком.

# 

# \## Главное правило

# 

# Нельзя ломать сохранение GS = 0x1D и криптохвост AI 91/92.

# 

# \## Строго запрещено

# 

# \- Нельзя читать ЧЗ только через обычный TextBox.

# \- Нельзя заменять GS на пробел, пустую строку, Enter, Tab или обычный видимый символ.

# \- Нельзя резать AI 92 по фиксированной длине.

# \- Нельзя заменять все спецсимволы подряд.

# \- Нельзя переписывать Raw Input / COM / парсер без тестов.

# \- Нельзя использовать реальные ЧЗ в тестах, логах и документации.

# 

# \## Ключевые файлы

# 

# Перед любыми изменениями обязательно читать:

# 

# \- src/DubliMark.Desktop/Services/RawInputScannerService.cs

# \- src/DubliMark.Desktop/Services/SerialScannerService.cs

# \- src/DubliMark.Core/Parsing/Gs1Parser.cs

# \- src/DubliMark.Core/Parsing/Gs1BarcodeEncoding.cs

# \- файлы генерации DataMatrix/PDF

# \- существующие тесты

# 

# \## Проверки после изменений

# 

# Всегда запускать:

# 

# ```bash

# dotnet restore

# dotnet build

# dotnet test

