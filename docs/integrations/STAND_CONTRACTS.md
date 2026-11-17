# Контракты тестового стенда

Проверено 17 ноября 2026 года. Документ описывает внешний стенд и границы подключения к нему. Первичная проверка документации дополнена реальным OIDC-входом: [настройки и результаты авторизации](KEYCLOAK_AUTH.md). Работа с заявками после входа пока не проверена.

## Адреса

| Назначение | Адрес |
| --- | --- |
| Keycloak, realm training | https://auth.makar-star.space/realms/training |
| OIDC discovery | https://auth.makar-star.space/realms/training/.well-known/openid-configuration |
| Service Desk | https://desk.makar-star.space |
| Swagger | https://desk.makar-star.space/docs/ |
| OpenAPI | https://desk.makar-star.space/docs/json |

Discovery подтверждает issuer из таблицы, адреса authorization, token, userinfo, JWKS и logout. Это публичные метаданные. Настройки конкретного клиента, его роли, MFA и разрешённые callback по ним не проверяются.

## Аутентификация и роли

Keycloak хранит учётные записи и управляет входом. Пароль вводится на стороне провайдера.

В документации стенда описан клиент student-service и учебный пример Authorization Code с PKCE S256, frontend callback и Bearer-токеном. Наш Backend также принимает access token Keycloak; реальный вход, обновление и выход проверены отдельно. Браузерный сценарий frontend ещё не проверен.

Основной механизм нашего API — JWT Bearer. Проверяются подпись через JWKS, issuer, audience student-service, срок и тип access token. Доступ разрешён всем ролям клиента student-service. Роли другого клиента или realm-роли не заменяют роль нашего приложения.

Роли разных клиентов не смешиваются:

| Клиент Keycloak | Назначение |
| --- | --- |
| service-desk | Доступ к внешним заявкам и настройкам Service Desk |
| student-service | Права собственного приложения |

Документация service-desk называет роли customer, support, support-lead и admin. Руководитель наследует поддержку. У student-service в исходном примере есть user и moderator; добавление admin и audience собственного API описано отдельными настройками. Наличие этих настроек на работающем клиенте пока не проверено.

Совпадение строки admin у двух клиентов не означает одинаковые права. Соответствие исходным РОЛ-01–РОЛ-05, полномочиям руководителя и делегированному назначению остаётся открытым.

## Запросы к Service Desk

Внешний API требует access token Keycloak с правильными issuer, audience service-desk, ролями клиента service-desk и team_slug. Токен для внешнего API должен содержать audience service-desk; токен для нашего API — audience student-service. Для обращения к обоим API нужны оба назначения.

Опубликованный OpenAPI имеет версию 3.0.3, версия описанного API — 0.2.0. В нём 14 операций:

| Метод | Путь | Назначение | Успешный ответ |
| --- | --- | --- | --- |
| GET | /api/me | Учётная запись, эффективная роль и teamId | 200 |
| GET | /rest/servicedeskapi/servicedesk/{deskId}/requesttype/{typeId}/field | Поля формы | 200 |
| GET | /rest/servicedeskapi/request | Список заявок | 200 |
| POST | /rest/servicedeskapi/request | Создание заявки | 201 |
| GET | /rest/servicedeskapi/request/{key} | Карточка заявки | 200 |
| GET | /rest/api/2/user/assignable/search | Доступные исполнители | 200 |
| PUT | /rest/api/2/issue/{key}/assignee | Назначение или снятие исполнителя | 204 |
| GET | /rest/api/2/issue/{key}/transitions | Разрешённые переходы статуса | 200 |
| POST | /rest/api/2/issue/{key}/transitions | Изменение статуса | 204 |
| GET | /rest/servicedeskapi/request/{key}/comment | Переписка | 200 |
| POST | /rest/servicedeskapi/request/{key}/comment | Новый комментарий | 201 |
| GET | /api/settings | Чтение настроек | 200 |
| PUT | /api/settings | Полная замена настроек | 200 |
| PATCH | /api/settings | Частичное изменение настроек | 200 |

Основные поля и ограничения:

- Список заявок использует start, limit от 1 до 100 и requestStatus: ALL_REQUESTS, OPEN_REQUESTS или CLOSED_REQUESTS. Ответ содержит values, size, start, limit и isLastPage.
- Создание требует serviceDeskId, requestTypeId и requestFieldValues. Обязательные поля следует получать из метаданных формы. ID 3 и 10001 в примерах относятся к учебному стенду.
- Внешний teamId — UUID из проверенного ответа /api/me. Его нельзя заменять строкой team_slug или без согласования считать TenantId нашего приложения.
- Для назначения передаётся JSON с полем name: логин выбранного кандидата или null. Кандидаты сначала запрашиваются через assignable/search.
- Для статуса сначала запрашиваются доступные transitions, затем передаётся объект transition с id. ID берётся из ответа, а не определяется по названию статуса.
- Комментарий содержит body и public. public=false обозначает внутреннюю заметку; права на неё проверяет внешний API.
- Закрытие в текущем описании стенда дополнительно требует fields.resolution.id. Значения переходов и resolution из примеров не считаются постоянными для других workflow.
- Работа с настройками использует свежий ETag и If-Match. При конфликте нужно перечитать настройки. Для частичной правки предназначен PATCH; PUT заменяет документ целиком.

## Webhook

Ожидаемый маршрут получателя в нашем Backend: POST /webhooks/service-desk. Сейчас он не реализован.

Контракт по документации:

- Content-Type: application/json.
- X-Training-Event-Id: стабильный UUID события.
- X-Training-Signature: префикс sha256= и 64 шестнадцатеричных символа.
- Алгоритм: HMAC-SHA256 по исходным байтам тела до разбора JSON.
- Выданный ключ используется как строка UTF-8, а не декодируется из hex. Значение хранится в User Secrets или переменной окружения.
- События: jira:issue_created и jira:issue_updated. Ключ заявки находится в issue.key, снимок данных — в issue.fields.
- После надёжного сохранения допустим ответ 200 или 204. По контракту отправителя любой 2xx подтверждает доставку.
- Сетевые ошибки, 429 и 5xx повторяются; другие неуспешные ответы завершают попытки ошибкой. Порядок доставки не гарантируется.
- Повтор одного Event-Id не должен повторно выполнять действие. Старое событие не должно перезаписывать более свежую карточку.
- Текст внутренних комментариев в уведомлении не передаётся.

Для localhost нужен публичный HTTPS-туннель с внешним портом 443. Удалённый стенд не сможет вызвать локальный адрес напрямую. Для браузерного OIDC callback на localhost туннель сам по себе не требуется; допустимый callback ещё нужно согласовать.

У общей службы один получатель webhook. Смена адреса влияет на другие команды. Доступ к настройкам не является разрешением менять их без отдельной согласованной задачи.

## Отличия от исходного ТЗ

| Исходные ID | Что требуется уточнить |
| --- | --- |
| PORT-04, НФТ-07, НФТ-10 | Выполнение входа, парольных политик и MFA проверяется через Keycloak. Собственная заготовка Identity не подтверждает эти функции. |
| РОЛ-01–РОЛ-05, НФТ-02 | Нужны отображение ролей, доверенный источник тенанта и проверка изоляции. Роли внешнего Service Desk не заменяют эту матрицу. |
| SD-15, SD-17 | В стенде семь статусов, а граф отличается от исходного. Например, заявитель может закрыть своё обращение из любого открытого статуса; в ТЗ проверка результата привязана к статусу «Проверка». |
| SD-05, SD-18–SD-24 | README стенда указывает отсутствие SLA. Настройки и отчёты исходного ТЗ нельзя считать предоставленными стендом. |

Покрытие активов, эскалаций, форм, отчётов и полный объём семестра требуют отдельного согласования. Исходные 102 ID сохраняются.

## Результаты проверки

При первичной сверке 17 ноября 2026 года выполнены чтение документации и анонимные GET-запросы:

| Проверка | Результат |
| --- | --- |
| README training-desk | HTTP 200 |
| docs/student-integration.md и три инструкции | HTTP 200 |
| Swagger /docs/ | HTTP 200 |
| OpenAPI /docs/json | HTTP 200, разобраны все 14 операций |
| Keycloak OIDC discovery | HTTP 200, issuer совпал с документацией |
| Service Desk GET /api/me без токена | HTTP 401 |
| Service Desk GET /rest/servicedeskapi/request?limit=1 без токена | HTTP 401 |

Во второй итерации 17 ноября с разрешения пользователя добавлены audience и роли student-service в access token, а тестовой учётной записи desk-admin назначена существующая роль student-service:user. Прочие настройки клиента и Service Desk не менялись. На трёх учётных записях проверены PKCE-вход, audience/роли, ответы Backend 200/401/403, обновление токена и стандартный logout. Прочитаны политики realm и метаданные MFA; обязательный MFA и соответствие требованиям не обеспечены. Создание и чтение заявок после входа, изменение статуса и доставка webhook пока не проверены. Подробности: [KEYCLOAK_AUTH.md](KEYCLOAK_AUTH.md).

## Вопросы перед реализацией

1. Какие origin/callback нужны отдельному frontend и требуется ли немедленный отзыв JWT после выхода? Реальный PKCE-вход и refresh проверены; logout завершает SSO и запрещает refresh, но прежний access token сохраняет срок действия (Q26).
2. Какие роли и claims Keycloak соответствуют пяти ролям ТЗ и откуда берётся тенант (Q28, Q30)?
3. Какие настройки Keycloak покрывают согласованные параметры паролей, блокировок и MFA (Q22, Q31)?
4. Кто и когда использует общий webhook либо выделяется ли отдельная служба для команды (Q32)?
5. Какие отличия workflow и отсутствующие блоки входят в объём семестра (Q29)?

## Источники

- [Описание стенда](https://github.com/vtyric/training-desk)
- [Общий порядок подключения](https://github.com/vtyric/training-desk/blob/main/docs/student-integration.md)
- [Пользователи и роли](https://github.com/vtyric/training-desk/blob/main/docs/students/01-keycloak-and-roles.md)
- [OpenID Connect](https://github.com/vtyric/training-desk/blob/main/docs/students/02-openid-connect.md)
- [Service Desk](https://github.com/vtyric/training-desk/blob/main/docs/students/03-service-desk.md)
- [Контракт и отличия от Jira](https://github.com/vtyric/training-desk/blob/main/docs/api-compatibility.md)
- [Опубликованный OpenAPI](https://desk.makar-star.space/docs/json)
