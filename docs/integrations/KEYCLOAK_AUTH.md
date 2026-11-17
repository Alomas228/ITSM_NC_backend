# Вход через Keycloak

Проверено 17 ноября 2026 года на реальном стенде. Backend принимает access token Keycloak; пароль, обновление токена и завершение SSO-сессии обрабатывает провайдер. Собственные маршруты логина, refresh и logout в API не нужны для выбранного публичного клиента.

## Настройки клиента

Realm: `training`. Client ID: `student-service`.

| Параметр | Проверенное значение |
| --- | --- |
| Client authentication | Off: публичный клиент без секрета |
| Standard flow | On |
| PKCE | S256 |
| Direct access grants | Off |
| Implicit flow | Off |
| Redirect URI стенда | `http://localhost:4300/auth/callback` |
| Web origin клиента Keycloak | `http://localhost:4300` |
| Post logout redirect URI | `http://localhost:4300/` |

Адреса выше уже зарегистрированы в общем клиенте. Это параметры стенда, а не реализация frontend или настройка CORS нашего API. Backend работает на своих адресах из launchSettings.json. Для отдельного frontend origin и точный callback нужно согласовать; свободный доступ CORS не включён. При общем reverse proxy браузер обращается к API через тот же origin.

С разрешения пользователя добавлены два mapper:

| Mapper | Настройка |
| --- | --- |
| Student service audience | Тип Audience; Included Client Audience = student-service; Add to access token = On |
| Student service roles | Тип User Client Role; Client ID = student-service; Token Claim Name = resource_access.student-service.roles; String; Multivalued = On; Add to access token = On |

Существующие mapper и настройки Service Desk сохранены. Тестовой учётной записи `desk-admin` назначена существующая роль `student-service:user`. Новая роль `admin` не создавалась. Название учётной записи не определяет полномочия нашего приложения.

Backend допускает любую непустую роль клиента student-service. Он проверяет подпись, issuer, audience, срок действия, sub и тип access token. Значение azp не заменяет audience. Токен для двух API содержит audience student-service и service-desk. Права конкретных бизнес-действий и соответствие пяти ролям исходного ТЗ ещё не определены.

## Проверка входа

1. Выполнить Authorization Code + PKCE S256 через готовую форму Keycloak. Проверять state, issuer ответа и nonce; обменивать код с исходным code_verifier. Пароль не передавать собственному API.
2. Отправить полученный access token в `Authorization: Bearer` на `GET /api/auth/me`. Не использовать ID token.
3. Обновлять токен через token endpoint Keycloak с grant_type=refresh_token и client_id=student-service. Для публичного клиента client secret не нужен. При успехе использовать новые токены из ответа; при invalid_grant требуется новый вход.
4. Завершать вход через стандартный end_session_endpoint Keycloak с id_token_hint. Для возврата передать зарегистрированный post_logout_redirect_uri. После завершения удалить локальные токены из памяти клиента.

Endpoint брать из discovery по адресу `https://auth.makar-star.space/realms/training/.well-known/openid-configuration`. Токены не сохранять в исходниках, HTTP-файлах, localStorage, отчётах и журналах. Для ручной проверки Backend использовать переменную окружения из README.

## Результат реальной проверки

Проверены три предоставленные тестовые учётные записи. Использовались готовая форма Keycloak и Authorization Code + PKCE; интерфейс приложения не создавался. Токены хранились в памяти проверочного процесса. Backend запускался на HTTP loopback; доверие к подписи и HTTPS-запросы к Keycloak не отключались. Проверка не заменяет браузерный сценарий отдельного frontend и его CORS.

| Сценарий | Результат |
| --- | --- |
| Без токена | 401 |
| Access token с student-service:user | 200; subject и роль совпали с токеном |
| Две учётные записи только с внешними ролями | 403 |
| ID token вместо access token | 401 для всех трёх учётных записей |
| Изменённая подпись | 401 для всех трёх учётных записей |
| Обновление токена | 200 у Keycloak; новый токен сохраняет ожидаемый доступ 200/403 |
| Стандартный OIDC logout | 200 у Keycloak |
| Refresh после logout | 400, invalid_grant |
| Повторный вход с prompt=none после logout | login_required |
| Ранее выданный access token после logout | Сохраняет прежний доступ до истечения срока |

Access token выдан на 300 секунд. Backend допускает рассинхронизацию часов до 30 секунд. Поэтому logout не обеспечивает немедленного запрета ранее выданного JWT; при необходимости такого запрета требуется отдельное решение о проверке активной сессии или отзыве. Блокировку пользователя и смену ролей после выдачи токена отдельно не проверяли.

Встроенные локальные тесты дополнительно проверяют неверные issuer/audience, срок, подпись, алгоритм, отсутствие sub/exp, тип токена и извлечение ролей. Они не используют рабочие учётные данные.

## Политики стенда и оставшиеся вопросы

Прочитаны настройки realm; они не менялись:

- Access token: 300 секунд; SSO idle: 1800 секунд; SSO max: 36000 секунд.
- Защита от перебора включена; failureFactor = 30, waitIncrementSeconds = 60, maxFailureWaitSeconds = 900. Фактическую блокировку намеренными ошибками не вызывали.
- Password policy не задана. Это не доказывает отсутствие отдельных ограничений LDAP.
- TOTP поддерживается, период 30 секунд; в browser flow настроена условная проверка. У трёх тестовых пользователей зарегистрирован только password; обязательного OTP при проверенных входах не было.

Эти параметры не подтверждают согласованные ранее пароль от 12 символов, блокировку после 5 ошибок, сессию 8 часов и обязательный TOTP для административных/инженерных ролей. Изменение общих политик требует отдельного согласования. Q22/Q31 остаются открытыми в части соответствия требованиям, а Q28/Q30 — в части тенантов и бизнес-полномочий.

Источники протокольных контрактов: [Keycloak OIDC](https://www.keycloak.org/securing-apps/oidc-layers), [инструкция стенда](https://github.com/vtyric/training-desk/blob/main/docs/students/02-openid-connect.md). Конкретные результаты выше получены при проверке стенда 17 ноября 2026 года.
