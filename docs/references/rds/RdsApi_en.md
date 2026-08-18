# RDS public interfaces

`baseurl` is `http://host:port` and `host` and `port` are set according to the parameters after field deployment. 

The request body and response body of the interface are both in `json` format and encoded in `UTF-8` format.



## Work site related interfaces

### Modify the status of a single work site

#### Interface Description

This `API` can modify the status of a given [workSite](about:blank).

####  Request

- Function: Modify the status of a specified work site
- Method: `POST`
- Interface Description: `/api/work-sites/updateFilledStatus`
- URL of the sample request

````http
POST http://host:8080/api/work-sites/updateFilledStatus
````

##### Request Parameters

| **Name**   | **Type** | **Description**                                           | **Required** |
| ---------- | -------- | --------------------------------------------------------- | ------------ |
| **siteId** | String   | Number of the target work site                            | Y            |
| **filled** | int      | work site occupancy status (0: not occupied, 1: occupied) | Y            |

##### Request Example

 Set the work site Loc-01 to occupied status : 

````json
{
    "siteId": "Loc-01",
    "filled": 1
}
````

#### Response

| **Name** | **Type** | **Description**                                              |
| -------- | -------- | ------------------------------------------------------------ |
| **code** | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**  | String   | API error code information                                   |
| **data** | Object   | The returned data object                                     |

##### Response data example

###### Responses Code 200

The request is successful and the Response body format is as follows: 

````json
{
    "code": 200,
    "msg": "Success",
    "data": null
}
````

###  Query Work Site List

#### Interface Introduction

This `API` can query the [work site](about:blank) list.

#### Request

- Function: Query the [work site](about:blank) list
- Method: POST
- Interface Description: `/api/work-sites/sites`
- URL of the sample request

```http
POST http://host:8080/api/work-sites/sites
```

##### Request Parameters

| **Name**       | **Type** | **Description**                                              | **Required** |
| -------------- | -------- | ------------------------------------------------------------ | ------------ |
| **siteId**     | String   | Work Site ID                                                 | N            |
| **area**       | String   | The area to which the work site belongs                      | N            |
| **locked**     | Integer  | work site lock status (0: unlocked, 1: locked)               | N            |
| **lockedBy**   | String   | Locking object of the work site                              | N            |
| **filled**     | Integer  | work site occupancy status (0: not occupied, 1: occupied)    | N            |
| **disabled**   | Integer  | work site disable status (0: not disabled, 1: disabled)      | N            |
| **syncFailed** | Integer  | work site synchronization status (0: no synchronization failure, 1: synchronization failure)） | N            |
| **content**    | String   | Goods in the work site                                       | N            |
| **type**       | Integer  | Work site type (0: logical site, 1: physical site, i.e. the site that actually exists on the map) | N            |
| **groupName**  | String   | Group name of the work site                                  | N            |

##### Request Example

###### Query all occupied and unlocked work sites

```json
{
    "filled": 1,
    "locked": 0
}
```

#### Response

| **Name**       | **Type** | **Description**                                              |
| -------------- | -------- | ------------------------------------------------------------ |
| **code**       | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**        | String   | API error code information                                   |
| **data**       | Object   | The returned data object                                     |
| **id**         | String   | Indicates the id of the work site, used for system identification of the work site |
| **siteId**     | String   | Indicates the name of the current work site                  |
| **locked**     | int      | Indicates whether the current work site is locked or not     |
| **lockedBy**   | String   | Indicates who is currently locking the work site             |
| **filled**     | int      | Indicates whether the work site is filled or not             |
| **disabled**   | int      | Indicates whether the current work site is disabled          |
| **syncFailed** | int      | Indicates whether the synchronization of the current work site has failed |
| **content**    | String   | Indicates the contents of the goods in the work site         |
| **area**       | String   | Indicates the area where the current work site is located    |
| **rowNum**     | String   | Indicates the row in which the current work site is located  |
| **colNum**     | String   | Indicates the col in which the current work site is located  |
| **level**      | String   | Indicates the level of the current work site                 |
| **depth**      | String   | Indicates the depth of the current work site                 |
| **no**         | String   | Indicates the number of the current work site                |
| **agvId**      | String   | Indicates the robot id that is using the work site           |
| **tags**       | String   | Indicates the tag of the current work site                   |
| **type**       | int      | Indicates the type of the current work site (0: logical work site, 1: physical work site) |
| **groupName**  | String   | Indicates the group where the current work site is located   |

##### Response data example

###### Responses Code 200

When the request is successful, the response body format is as follows: 

```json
{
   "code":200,
   "msg":"Success",
   "data":[
      {
         "id":"402888a47f305d53017f305d8c560000",
         "siteId":"B01",
         "locked":0,
         "lockedBy":"",
         "filled":1,
         "disabled":0,
         "syncFailed":0,
         "content":"",
         "area":"new",
         "rowNum":null,
         "colNum":null,
         "level":null,
         "depth":null,
         "no":null,
         "agvId":null,
         "tags":null,
         "type":1,
         "groupName":"filled"
      },
      {
         "id":"402888a47f305d53017f305d8c860001",
         "siteId":"B02",
         "locked":0,
         "lockedBy":"",
         "filled":1,
         "disabled":0,
         "syncFailed":0,
         "content":"",
         "area":"new",
         "rowNum":null,
         "colNum":null,
         "level":null,
         "depth":null,
         "no":null,
         "agvId":null,
         "tags":null,
         "type":1,
         "groupName":"filled"
      }
   ]
}
```

###  Paging the list of library positions 

#### Interface Introduction

This `API` can paginate the [library](about:blank) list.

#### Request

- Function: Paging query [library](about:blank) list
- Method: POST
- Interface Description: `/api/work-sites/sitesPageAble`
- URL of the sample request

```http
POST http://host:8080/api/work-sites/sitesPageAble
```

##### Request Parameters

| **Name**        | **Type** | **Description**                                              | **Required** |
| --------------- | -------- | ------------------------------------------------------------ | ------------ |
| **currentPage** | int      | Indicates the result of querying the current page            | Y            |
| **pageSize**    | int      | Indicates paging of all results by pageSize records per page | Y            |
| **queryParam**  | Object   | Query parameter object                                       | Y            |
| **siteId**      | String   | Work site ID                                                 | N            |
| **area**        | String   | The area to which the work site belongs                      | N            |
| **locked**      | int      | work site lock status (0: unlocked, 1: locked)               | N            |
| **lockedBy**    | String   | Locking the object of the work site                          | N            |
| **filled**      | int      | Work site occupancy status (0: not occupied, 1: occupied)    | N            |
| **disabled**    | int      | Work site disable status (0: not disabled, 1: disabled)      | N            |
| **syncFailed**  | int      | Work site synchronization status (0: no synchronization failure, 1: synchronization failure) | N            |
| **content**     | String   | Goods in the work site                                       | N            |
| **type**        | int      | Indicates the type of the current work site (0: logical work site, 1: physical work site) | N            |
| **groupName**   | String   | Indicates the group where the current work site is located   | N            |

##### Request Example

```json
{
    "currentPage":2,
    "pageSize":2,
    "queryParam": {
        "filled":0,
        "locked":1
    }
}
```

#### Response

| **Name**        | **Type** | **Description**                                              |
| --------------- | -------- | ------------------------------------------------------------ |
| **code**        | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**         | String   | API error code information                                   |
| **data**        | Object   | The returned data object                                     |
| **totalCount**  | int      | Indicates the total number of tasks queried                  |
| **currentPage** | int      | Page number                                                  |
| **pageSize**    | int      | Page size                                                    |
| **totalPage**   | int      | Displays all tasks queried, divided into totalPage pages     |
| **pageList**    | List     | A number of tasks are recorded for the current page          |
| **id**          | String   | Indicates the id of the work site, used for system identification of the work site |
| **siteId**      | String   | Indicates the name of the current work site                  |
| **locked**      | int      | Indicates whether the current work site is locked or not     |
| **lockedBy**    | String   | Indicates who is currently locking the work site             |
| **filled**      | int      | Indicates whether the work site is filled or not             |
| **disabled**    | int      | Indicates whether the current work site is disabled          |
| **syncFailed**  | int      | Indicates whether the synchronization of the current work site has failed |
| **content**     | String   | Indicates the contents of the goods in the work site         |
| **area**        | String   | Indicates the area where the current work site is located    |
| **rowNum**      | String   | Indicates the row in which the current work site is located  |
| **colNum**      | String   | Indicates the col in which the current work site is located  |
| **level**       | String   | Indicates the level of the current work site                 |
| **depth**       | String   | Indicates the depth of the current work site                 |
| **no**          | String   | Indicates the number of the current work site                |
| **agvId**       | String   | Indicates the robot id that is using the work site           |
| **tags**        | String   | Indicates the tag of the current work site                   |
| **type**        | int      | Indicates the type of the current work site (0: logical work site, 1: physical work site) |
| **groupName**   | String   | Indicates the group where the current work site is located   |

##### Response data example

###### Responses Code 200

```json
{
    "code": 200,
    "msg": "Success",
    "data": {
        "totalCount": 28,
        "currentPage": 2,
        "pageSize": 2,
        "totalPage": 14,
        "pageList": [
            {
                "id": "40289fa2804b090b01804b092b750003",
                "siteId": "B04",
                "locked": 0,
                "lockedBy": "",
                "filled": 0,
                "disabled": 0,
                "syncFailed": 0,
                "content": "",
                "area": "new",
                "rowNum": null,
                "colNum": null,
                "level": null,
                "depth": null,
                "no": null,
                "agvId": null,
                "tags": null,
                "type": 1,
                "groupName": "filled"
            },
            {
                "id": "40289fa2804b090b01804b092b780004",
                "siteId": "B05",
                "locked": 0,
                "lockedBy": "",
                "filled": 0,
                "disabled": 0,
                "syncFailed": 0,
                "content": "",
                "area": "new",
                "rowNum": null,
                "colNum": null,
                "level": null,
                "depth": null,
                "no": null,
                "agvId": null,
                "tags": null,
                "type": 1,
                "groupName": "klj"
            }
        ]
    }
}
```



### Set the worksite to be occupied by RDS

#### Interface Introduction

This `API` can set the occupant of the worksite to RDS

#### Request

- Function：Set the occupant of the worksite to RDS

- Method：POST

- Interface Description：`/api/work-sites/releaseWorksiteHolder`

- URL of the sample request

  ```http
  POST http://host:8080/api/work-sites/releaseWorksiteHolder
  ```

##### Request Parameters

|  Name  |  Type  | Description | Required |
| :----: | :----: | :---------: | :------: |
| siteId | String |   Worksite Id   |    Y    |

##### Request Example

```json
{
    "siteId": "Loc-02"
}
```



#### Response

| Name |  Type  |                         Description                          |
| :--: | :----: | :----------------------------------------------------------: |
| code |  int   | API error codes, see [Error Code List](#errorcodelist) for details |
| msg  | String |                  API error code information                  |
| data | Object |                   The returned data object                   |

##### Response data example

###### Response Code 200

```json
{
    "code": 200,
    "msg": "Success",
    "data": null
}
```



### Set the worksite to be occupied by Core

#### Interface Introduction

This `API` can set the occupant of the worksite to Core

#### Request

- Function：Set the occupant of the worksite to Core

- Method：POST

- Interface Description：`/api/work-sites/holdWorksiteByCore`

- URL of the sample request

  ```http
  POST http://host:8080/api/work-sites/holdWorksiteByCore
  ```

##### Request Parameters

|  Name  |  Type  | Description | Required |
| :----: | :----: | :---------: | :------: |
| siteId | String |   WorkSite Id    |    Y    |

##### Request Example

```json
{
    "siteId": "Loc-02"
}
```



#### Response

| Name |  Type  |                         Description                          |
| :--: | :----: | :----------------------------------------------------------: |
| code |  int   | API error codes, see [Error Code List](#errorcodelist) for details |
| msg  | String |                  API error code information                  |
| data | Object |                   The returned data object                   |

##### Response data example

###### Response Code 200

```json
{
    "code": 200,
    "msg": "Success",
    "data": null
}
```




## WindTask mission-related interfaces

### Paging Query Wind Task Example List

#### Interface Introduction

This `API` can paginate the list of [wind task](about:blank) examples

#### Request

- Function: Paging query [wind task](about:blank) Example list
- Method: POST
- Interface Description: `/api/queryWindTask`
- URL of the sample request

~~~http
POST http://host:8080/api/queryWindTask
~~~

##### Request header

| **Name**     | **Type** | **Description**                                              | **Required** |
| ------------ | -------- | ------------------------------------------------------------ | ------------ |
| **language** | String   | Indicates the language, and Chinese is used by default if it is not passed. (zh: Chinese, en: English, ja-jp: Japanese) | N            |

##### Request Parameters

| **Name**         | **Type** | **Description**                                              | **Required** |
| ---------------- | -------- | ------------------------------------------------------------ | ------------ |
| **currentPage**  | int      | Page number                                                  | N            |
| **pageSize**     | int      | Page size                                                    | Y            |
| **queryParam**   | Object   | Query parameter object                                       | Y            |
| **taskId**       | String   | Task id                                                      | N            |
| **taskLabel**    | String   | Task name                                                    | N            |
| **taskRecordId** | String   | Task instance id (transport task id)                         | N            |
| **status**       | int      | Task status (1000:running; 1001:terminated; 1002:suspended; 1003:finished; 1004:abnormally finished;1005:Restart exception;1006:Abnormal Interruption) | N            |
| **agvId**        | String   | Robot id                                                     | N            |
| **startOn**      | String   | Task start time (format: yyyy-MM-dd HH:mm:ss)(Filtered by task creation time) | N            |
| **endOn**        | String   | Task end time (format: yyyy-MM-dd HH:mm:ss)(Filtered by task creation time) | N            |
| **isOrderDesc**  | Boolean  | Whether to sort by start time (default false;false/true:backward/forward order) | N            |

##### Request Example

###### Query the English environment, the task status of the end of the task, sorted in reverse chronological order, paged by 2 records per page, the current query page 2 of the results

**Request header**

| **key**      | **value** |
| ------------ | --------- |
| **language** | en        |

**Request Body**

~~~json
{
    "currentPage":2,
    "pageSize":2,
    "queryParam": {
        "taskId":"",
        "taskRecordId":"",
        "status":1003,
        "agvId":"",
        "startDate":"",
        "endDate":"",
        "isOrderDesc":true
    }
}
~~~

#### Response

| **Name**              | **Type** | **Description**                                              |
| --------------------- | -------- | ------------------------------------------------------------ |
| **code**              | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**               | String   | API error code information                                   |
| **data**              | Object   | The returned data object                                     |
| **totalCount**        | int      | Indicates the total number of tasks queried                  |
| **currentPage**       | int      | indicates the page number of the current result, if the parameter currentPage is null, the return value is null |
| **pageSize**          | int      | Indicates the maximum number of tasks that can be recorded on the current page |
| **id**                | String   | Windtask instance ID                                         |
| **status**            | String   | Windtask instance status                                     |
| **agvId**             | String   | agvId to perform the task                                    |
| **defLabel**          | String   | Windtask name                                                |
| **createOn**          | String   | Windtask instance creation time                              |
| **endOn**             | String   | Windtask instance run end time                               |
| **firstExecutorTime** | String   | Robot start execution time                                   |
| **executorTime**      | String   | Total robot time spent                                       |
| **endedReason**       | String   | Windtask description                                         |
| **outOrderNo**        | String   | External Order Number                                        |
| **defId**             | String   | Task ID                                                      |

##### Response data example

###### Responses Code 200

~~~json
{
    "code": 200,
    "msg": "Success",
    "data": {
        "totalCount": 24,
        "currentPage": 2,
        "pageSize": 2,
        "totalPage": 12,
        "pageList": [
            {
                "id": "429ebb14-f77f-4a5a-b7ac-2c1957894c79",
                "status": 1003,
                "agvId": "",
                "defLabel": "test",
                "defId": "",
                "createdOn": "2023-05-30 13:10:40",
                "endedOn": "2023-05-30 13:10:40",
                "firstExecutorTime": "",
                "executorTime": 0,
                "endedReason": "[RootBp]End",
                "outOrderNo": null
            }
        ]
    }
}
~~~

### Query block run list and task status based on task instance Id

#### Interface Introduction

This `API` gets the status of the specified [task instance](about:blank) and the list of [block](about:blank) runs.

#### Request

- Function: Get the status of the specified task instance and the list of block runs.
- Method: POST
- Interface Description: `/api/queryBlocksByTaskId`
- URL of the sample request

```http
POST http://host:8080/api/queryBlocksByTaskId
```

##### Request header

| **Name**     | **Type** | **Description**                                              | **Required** |
| ------------ | -------- | ------------------------------------------------------------ | ------------ |
| **language** | String   | Indicates the language, and Chinese is used by default if it is not passed. (zh: Chinese, en: English, ja-jp: Japanese) | N            |

##### Request Parameters

| **Name**         | **Type** | **Description**                | **Required** |
| ---------------- | -------- | ------------------------------ | ------------ |
| **taskRecordId** | String   | Indicates the Task instance ID | Y            |

##### Request Example

###### Query the block run list and task status of task instance ID c797a1d3-2aef-4bf6-8874-cf5d1c5602f7 in English environment

**Request header**

| **key**      | **value** |
| ------------ | --------- |
| **language** | en        |

**Request Body**

```json
{
    "taskRecordId":"c797a1d3-2aef-4bf6-8874-cf5d1c5602f7"
}
```

#### Response

| **Name**        | **Type** | **Description**                                              |
| --------------- | -------- | ------------------------------------------------------------ |
| **code**        | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**         | String   | API error code information                                   |
| **data**        | Object   | The returned data object                                     |
| **blockList**   | List     | Indicates a list of block runs for the current task instance |
| **blockName**   | int      | Indicates the name of the block                              |
| **status**      | String   | Indicates the running status of the block (1000: block running; 1001: block terminated; 1002: block paused; 1003: block completed; 1004: block abnormally stopped; 1005: block not running; 1006: parallel block finished; 1007: block waiting) |
| **startedOn**   | Date     | Indicates the start time of the block                        |
| **endedOn**     | Date     | Indicates the end time of the block                          |
| **endedReason** | String   | Indicates the reason for the end of the block                |
| **blockLabel**  | String   | Indicates the label of the block                             |
| **taskStatus**  | String   | Indicates the task running status <br/>1000: Running.<br/>1001: Terminate.<br/>1002: Pause.<br/>1003: Finished.<br/>1004: Exceptional End.<br/>1005:  Restart exception.<br/>1006:  Abnormal Interruption.<br/>1007:  End Manually . |

##### Response data example

###### Responses Code 200

```json
{
    "code": 200,
    "msg": "Success",
    "data": {
        "blockList": [
            {
                "blockName": "RootBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:56:57.000+00:00",
                "endedOn": "2022-05-23T04:58:00.000+00:00",
                "endedReason": "[RootBp]End Of Task",
                "blockLabel": "start block"
            },
            {
                "blockName": "QueryIdleSiteBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:56:57.000+00:00",
                "endedOn": "2022-05-23T04:56:58.000+00:00",
                "endedReason": "[QueryIdleSiteBp]Block Finish",
                "blockLabel": "Worksite"
            },
            {
                "blockName": "QueryIdleSiteBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:56:58.000+00:00",
                "endedOn": "2022-05-23T04:56:58.000+00:00",
                "endedReason": "[QueryIdleSiteBp]Block Finish",
                "blockLabel": "Worksite"
            },
            {
                "blockName": "IfBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:56:58.000+00:00",
                "endedOn": "2022-05-23T04:57:59.000+00:00",
                "endedReason": "[IfBp]Block Finish",
                "blockLabel": "Program Flow"
            },
            {
                "blockName": "CSelectAgvBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:56:58.000+00:00",
                "endedOn": "2022-05-23T04:57:58.000+00:00",
                "endedReason": "[CSelectAgvBp]Block Finish，agvId=sim_02",
                "blockLabel": "Core"
            },
            {
                "blockName": "CAgvOperationBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:57:18.000+00:00",
                "endedOn": "2022-05-23T04:57:43.000+00:00",
                "endedReason": "[CAgvOperationBp]Block Finish",
                "blockLabel": "Core"
            },
            {
                "blockName": "CAgvOperationBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:57:43.000+00:00",
                "endedOn": "2022-05-23T04:57:58.000+00:00",
                "endedReason": "[CAgvOperationBp]Block Finish",
                "blockLabel": "Core"
            },
            {
                "blockName": "IfBp",
                "status": 1003,
                "startedOn": "2022-05-23T04:57:59.000+00:00",
                "endedOn": "2022-05-23T04:58:00.000+00:00",
                "endedReason": "[IfBp]Block Finish",
                "blockLabel": "Program Flow"
            }
        ],
        "taskStatus": 1003
    }
}
```

### Cancel Or Stop WindTask

#### Interface Introduction

This `API` can cancel/stop task and choose whether to unlock the task' s related work sites.

#### Request

- Function: Cancel/Stop task and choose whether to unlock the task' s related work sites.
- Method: POST
- Interface Description: `/api/stop-task`
- URL of the sample request

```http
POST http://host:8080/api/stop-task
```

##### Request Parameters

| **Name**         | **Type** | **Description**                                              | **Required**                                                 |
| ---------------- | -------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **releaseSite**  | int      | Whether to unlock task-related work sites at the same time (0: No, 1: Yes) | Y                                                            |
| **taskRecordId** | String   | The stopped task instance ID                                 | Yes, and the taskLabel field must be optional.               |
| **taskLabel**    | String   | The stopped task name                                        | Yes, and the taskRecordId field must be optional; if taskRecordId is filled in, the field is invalid. |

##### Request Example

###### Stop the task with task instance ID `bd536d07-b089-4c50-b058-e7cdcfd3a194` and unlocks the  task' s related work sites

```json
{
  "releaseSite": 1,
  "taskRecordId": "bd536d07-b089-4c50-b058-e7cdcfd3a194"
}
```

#### Response

| **Name** | **Type** | **Description**                                              |
| -------- | -------- | ------------------------------------------------------------ |
| **code** | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**  | String   | API error code information                                   |
| **data** | Object   | The returned data object                                     |

##### Response Example

###### Responses Code 200

```json
{
    "code": 200,
    "msg": "Success",
    "data": null
}
```

## Stat

Record AGV status, AGV orders and wind task at different time periods. Note that stat-related Functionality is built on RDS application, all stat data will not be recorded even if the vehicle is still running.

### AGV Status

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- description: query AGV status duration.
- method: POST
- endpoint: `/api/stat/agvStatus`

Example URL request:

```http
POST "http://host:8080/api/stat/agvStatus"
```

##### Request Body

|   Name   | Type   | Description                                                  | Required |
| :------: | :----- | :----------------------------------------------------------- | :------: |
|  start   | String | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|   end    | String | A string for end time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|  level   | String | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. |    Y     |
|  types   | String | A string of [vehicle stat type](#vehicleStatTypes), multiple types separated by a comma ','. |    Y     |
| vehicle  | String | The vehicle id string to be filtered, all vehicles by default. |    N     |
| timeUnit | String | The unit of time with the value `sec` or `min` or `hour`, `millisecond` by default. |    N     |

##### Request Example

time interval: [start, end)

```json
{
    "start": "1970-01-01 12",
    "end": "1970-01-01 14",
    "level": "Hour",
    "types": "VehicleIdleTime",
    "vehicle": "AGV-01",
    "timeUnit": ""
}
```

<a name='vehicleStatTypes'>Vehicle Stat Type</a>

|           Type           | Description                                                  |
| :----------------------: | :----------------------------------------------------------- |
|    `VehicleIdleTime`     | Idle time of a vehicle. The IDLE doesn't means that the vehicle is stopped, it's also IDLE when executing a non-business order like a park or a charge order which could be interrupted by business orders. |
|  `VehicleExecutingTime`  | Executing time of a vehicle. The EXECUTING doesn't means that the vehicle is moving, it's also EXECUTING while waiting for a exclusive area occupied by another AGV and other similar circumstances after being assigned an order. |
| `VehicleUnavailableTime` | Unavailable time of a vehicle that is undispatchable or not under the control of RDS Core. |
|  `VehicleChargingTime`   | Charging time of a vehicle.                                  |
|    `VehicleErrorTime`    | Error time of a vehicle.                                     |
|   `VehicleOfflineTime`   | Offline time of a vehicle. All the situations that a vehicle disconnected from the RDS Core. |

**Note**: IDLE, EXECUTING status exists only when a vehicle is dispatchable and under the control of RDS Core.

#### Response

Regardless of network and other unpredictable exceptions, response http status code always return 200. And the business code in http body is 200 if request successfully, otherwise 9000+. Refer to the business [Error Code List](#errorcodelist).

##### Response Body

| Name |  Type  |   Description    |
| :--: | :----: | :--------------: |
| code |  int   |  business code   |
| msg  | String | business message |
| data |  List  |   request data   |

##### Response Example

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "level": "Hour",
            "time": "1970-01-01 12",
            "type": "VehicleIdleTime",
            "value": 0,
            "id": "AGV-01"
        },
        {
            "level": "Hour",
            "time": "1970-01-01 13",
            "type": "VehicleIdleTime",
            "value": 0,
            "id": "AGV-01"
        }
    ]
}
```

The field `value` of data in the response body is the duration from the field `time` to the next time point with the same `level`.

### AGV Alarm

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- description: query AGV status duration.
- method: POST
- endpoint: `/api/stat/agvAlarm`

Example URL request:

```http
POST "http://host:8080/api/stat/agvAlarm"
```

##### Request Body

|   Name   | Type   | Description                                                  | Required |
| :------: | :----- | :----------------------------------------------------------- | :------: |
|  start   | String | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|   end    | String | A string for end time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|  level   | String | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. |    Y     |
|  types   | String | A string of [vehicle stat type](#vehicleStatTypes), multiple types separated by a comma ','. |    Y     |
| vehicle  | String | The vehicle id string to be filtered, all vehicles by default. |    N     |
| timeUnit | String | The unit of time with the value `sec` or `min` or `hour`, `millisecond` by default. |    N     |

##### Request Example

time interval: [start, end)

```json
{
    "start": "1970-01-01 12",
    "end": "1970-01-01 14",
    "level": "Hour",
    "types": "AlarmsErrorsNum",
    "vehicle": "AGV-01",
    "timeUnit": ""
}
```

<a name='vehicleStatTypes'>Vehicle Stat Type</a>

|         Type         | Description                     |
| :------------------: | :------------------------------ |
| `AlarmsWarningsTime` | The AGV is at the warning time. |
|  `AlarmsErrorsTime`  | The AGV is at the error time.   |
|  `AlarmsFatalsTime`  | The AGV is at the fatal time.   |
| `AlarmsWarningsNum`  | Number of AGV on warning        |
|  `AlarmsErrorsNum`   | Number of AGV on error          |
|  `AlarmsFatalsNum`   | Number of AGV on fatal          |

**Note**: The AGV statistics function is based on the normal start-up of the RDS.

#### Response

Regardless of network and other unpredictable exceptions, response http status code always return 200. And the business code in http body is 200 if request successfully, otherwise 9000+. Refer to the business [Error Code List](#errorcodelist).

##### Response Body

| Name |  Type  |   Description    |
| :--: | :----: | :--------------: |
| code |  int   |  business code   |
| msg  | String | business message |
| data |  List  |   request data   |

##### Response Example

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "level": "Hour",
            "time": "1970-01-01 12",
            "type": "AlarmsErrorsNum",
            "value": 0,
            "id": "AGV-01"
        },
        {
            "level": "Hour",
            "time": "1970-01-01 13",
            "type": "AlarmsErrorsNum",
            "value": 0,
            "id": "AGV-01"
        }
    ]
}
```

In the data returned, if the type ends with Time, the parameter value indicates the accumulated time. If type ends with Num, the value parameter indicates the cumulative amount of time in the unit specified in the request parameter, the default being milliseconds. The period of the statistics is from the start of the `time` field to the end of the next period of the same level.

### AGV Orders

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- description: stat AGV orders.
- method: POST
- endpoint: `/api/stat/agvOrders`

Example URL request:

```http
POST "http://host:8080/api/stat/agvOrders"
```

##### Request Body

|  Name   | Type   | Description                                                  | Required |
| :-----: | :----- | :----------------------------------------------------------- | :------: |
|  start  | String | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|   end   | String | A string for end time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|  level  | String | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. |    Y     |
|  types  | String | A string of [order stat type](#orderStatTypes), multiple types separated by a comma ','. |    Y     |
| vehicle | String | The vehicle id string to be filtered, all vehicles by default. |    N     |

##### Request Example

time interval: [start, end)

```json
{
    "start": "1970-01-01 12",
    "end": "1970-01-01 14",
    "level": "Hour",
    "types": "OrderBusinessOdo",
    "vehicle": "AGV-01"
}
```

<a name='orderStatTypes'>Order Stat Type</a>

|        Type        | Description                                                  |
| :----------------: | :----------------------------------------------------------- |
| `OrderBusinessOdo` | The total business odometer during a time period.            |
| `OrderCreatedNum`  | The total number of orders created during a time period. Filtering with a `vehicle` will return empty for `OrderCreatedNum` because an order may not have been assigned a vehicle when created. |
| `OrderFinishedNum` | The total number of orders finished successfully.            |
| `OrderStoppedNum`  | The total number of orders haven been terminated.            |
|  `OrderTakingNum`  | The total number of orders taken by robots                   |

#### Response

Regardless of network and other unpredictable exceptions, response http status code always return 200. And the business code in http body is 200 if request successfully, otherwise 9000+. Refer to the business [Error Code List](#errorcodelist).

##### Response Body

| Name |  Type  |   Description    |
| :--: | :----: | :--------------: |
| code |  int   |  business code   |
| msg  | String | business message |
| data |  List  |   request data   |

##### Response Example

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "level": "Hour",
            "time": "1970-01-01 12",
            "type": "OrderBusinessOdo",
            "value": 123,
            "id": "AGV-01"
        },
        {
            "level": "Hour",
            "time": "1970-01-01 13",
            "type": "OrderBusinessOdo",
            "value": 123,
            "id": "AGV-01"
        }
    ]
}
```

The field `value` of data in the response body is the total number or odometer from the field `time` to the next time point with the same `level`.

### Query the list of the number of tasks created/completed/terminated

#### Interface Introduction

This `API` can query the list of the number of tasks with created/completed/terminated status, containing only task data and no robot-related information.

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- description: Query the list of tasks with creation/completion/termination status
- method: POST
- Interface Description: `/api/stat/taskList`
- URL of the sample request

```http
POST http://host:8080/api/stat/taskList
```

##### Request Body

| **Name**  | **Type** | **Description**                                              | **Required** |
| --------- | -------- | ------------------------------------------------------------ | ------------ |
| **level** | String   | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. | Y            |
| **types** | String   | A string of task stat type, multiple types separated by a comma ','（WindTaskCreatedNum: Task created number,WindTaskFinishedNum: Task finished number,WindTaskStoppedNum: Task stopped number,WindTaskExceptionNum：Task exception number） | Y            |
| **start** | String   | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. | Y            |
| **end**   | String   | A string for end time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. | Y            |

##### Request Example

###### Query the number of task stopped for 2019 (inclusive) - 2022 (exclusive)

```json
{
    "level": "Year",
    "types":"WindTaskStoppedNum",
    "start":"2019",
    "end":"2022"
}
```

#### Response

| **Name**  | **Type** | **Description**                                              |
| --------- | -------- | ------------------------------------------------------------ |
| **code**  | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**   | String   | API error code information                                   |
| **data**  | Object   | The returned data object                                     |
| **level** | String   | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. |
| **time**  | String   | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |
| **type**  | String   | A string of task stat type. (WindTaskCreatedNum: Task created number,WindTaskFinishedNum: Task finished number,WindTaskStoppedNum: Task stopped number） |
| **value** | int      | Indicates the number of tasks                                |
| **id**    | String   | None used                                                    |

##### Response data example

###### Responses Code 200

The request is successful and the Response body format is as follows: 

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "level": "Year",
            "time": "2019",
            "type": "WindTaskStoppedNum",
            "value": 414.00,
            "id": ""
        },
        {
            "level": "Year",
            "time": "2020",
            "type": "WindTaskStoppedNum",
            "value": 14712.00,
            "id": ""
        },
        {
            "level": "Year",
            "time": "2021",
            "type": "WindTaskStoppedNum",
            "value": 23703.00,
            "id": ""
        }
    ]
}
```

### Average Execution Time of Wind Task

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- description: query AGV status duration.
- method: POST
- endpoint: `/api/stat/windTaskAvgCosts`

Example URL request:

```http
POST "http://host:8080/api/stat/windTaskAvgCosts"
```

##### Request Body

|   Name    | Type   | Description                                                  | Required |
| :-------: | :----- | :----------------------------------------------------------- | :------: |
|   start   | String | A string for start time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|    end    | String | A string for end time with the format of `yyyy-MM-dd HH` or `yyyy-MM-dd` or `yyyy-MM` or `yyyy` depends on the `level` field. |    Y     |
|   level   | String | A string indicating the time interval with the value `Hour` or `Day` or `Month` or `Year`. |    Y     |
| taskLabel | String | The task label string to be filtered, all tasks by default.  |    N     |
| timeUnit  | String | The unit of time with the value `sec` or `min` or `hour`, `millisecond` by default. |    N     |

##### Request Example

time interval: [start, end)

```json
{
    "start": "2022-10-18",
    "end": "2022-10-19",
    "level": "Day",
    "timeUnit": "min",
    "taskLabel": "test-01"
}
```

#### Response

Regardless of network and other unpredictable exceptions, response http status code always return 200. And the business code in http body is 200 if request successfully, otherwise 9000+. Refer to the business [Error Code List](#errorcodelist).

##### Response Body

| Name |  Type  |   Description    |
| :--: | :----: | :--------------: |
| code |  int   |  business code   |
| msg  | String | business message |
| data |  List  |   request data   |

##### Response Example

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "level": "Day",
            "time": "2022-10-18",
            "type": "WindTaskAvgCostTime",
            "value": 70.67,
            "id": "test-01"
        }
    ]
}
```

The field `value` of data in the response body is the duration from the field `time` to the next time point with the same `level`.

### Query the list of tasks completed by the robot

#### Interface Introduction

This `API` can query the list of tasks completed by the robot.

Statistical Period: For the level "Hour", data is updated every ten minutes. For the levels "Day", "Month", and "Year", data is updated every fifteen minutes.

#### Request

- Function: Query the list of tasks completed by the robot
- Method: POST
- Interface Description: `/api/stat/agvTaskSuccessList`
- URL of the sample request

```http
POST http://host:8080/api/stat/agvTaskSuccessList
```

##### Request Parameters

| **Name**  | **Type** | **Description**                                           | **Required** |
| --------- | -------- | --------------------------------------------------------- | ------------ |
| **start** | String   | The start time of the statistic, in `yyyy-MM-dd ` format. | Y            |
| **end**   | String   | The end time of the statistic, in `yyyy-MM-dd ` format.   | Y            |

##### Request Example

Query the list of tasks completed from `2020-01-01 00:00:00` to `2022-07-19 23:59:59`.

```json
{
    "start":"2020-01-01",
    "end":"2022-07-19"
}
```

#### Response

| **Name**  | **Type** | **Description**                                              |
| --------- | -------- | ------------------------------------------------------------ |
| **code**  | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**   | String   | API error code information                                   |
| **data**  | Object   | The returned data object List                                |
| **agvId** | String   | Robot id                                                     |
| **tasks** | List     | Number of robot task objects                                 |

##### Response data example

###### Responses Code 200

The request is successful and the Response body format is as follows: 

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "agvId": "sim_05",
            "tasks": {
                "ww": 132,
                "test": 3,
                "easyTask": 375
            }
        },
        {
            "agvId": "AMB-02",
            "tasks": {
                "ww": 0,
                "test": 87,
                "easyTask": 10
            }
        },
        {
            "agvId": "AMB-01",
            "tasks": {
                "ww": 40,
                "test": 231,
                "easyTask": 0
            }
        },
        {
            "agvId": "AMB-03",
            "tasks": {
                "ww": 0,
                "test": 0,
                "easyTask": 654
            }
        }
    ]
}
```

###  Task retention list

#### Interface Introduction

This `API` can query the task retension list.

#### Request

- Function: Query Task Retension List
- Method: POST
- Interface Description: `/api/stat/taskRetentionList`
- URL of the sample request

```http
POST http://host:8080/api/stat/taskRetentionList
```

##### Request Parameters

| **Name**           | **Type** | **Description**                                              | **Required** |
| ------------------ | -------- | ------------------------------------------------------------ | ------------ |
| **currentPage**    | int      | Indicates the result of querying the current page            | Y            |
| **pageSize**       | int      | Indicates paging of all results by pageSize records per page | Y            |
| **queryParam**     | Object   | Query parameter object                                       | Y            |
| **retentionHours** | String   | Retention hours, Unit: hours                                 | Y            |

##### Request Example

###### Paging the list of tasks with a retension time of more than 1 hour

```json
{
    "currentPage": 1,
    "pageSize": 10,
    "queryParam": {
        "retentionHours": 1
    }
}
```

#### Response

| **Name**        | **Type** | **Description**                                              |
| --------------- | -------- | ------------------------------------------------------------ |
| **code**        | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**         | String   | API error code information                                   |
| **data**        | Object   | The returned data object                                     |
| **totalCount**  | int      | Indicates the total number of tasks queried                  |
| **currentPage** | int      | Page number                                                  |
| **pageSize**    | int      | Page size                                                    |
| **totalPage**   | int      | Displays all tasks queried, divided into totalPage pages     |
| **pageList**    | List     | A number of tasks are recorded for the current page          |
| **id**          | String   | Indicates the id of the wind task record                     |
| **defLabel**    | String   | Wind task Label                                              |
| **createOn**    | String   | Task run time                                                |
| **status**      | int      | Task status (1000:running; 1001:terminated; 1002:suspended; 1003:finished; 1004:abnormally finished) |
| **agvId**       | String   | Agv id                                                       |

##### Response data example

###### Responses Code 200

The request is successful and the Response body format is as follows: 

```json
{
    "code": 200,
    "msg": "Success",
    "data": {
        "totalCount": 1,
        "currentPage": 1,
        "pageSize": 10,
        "totalPage": 1,
        "pageList": [
            {
                "id": "bfc88151-15d6-43df-8771-ce03f7c29e19",
                "defLabel": "test",
                "createdOn": "2022-07-19T03:38:56.709+00:00",
                "status": 1000,
                "agvId": null
            }
        ]
    }
}
```


## Operator related interfaces

### Get a list of outstanding demand notes

#### Interface Introduction

Get a list of all outstanding demand tickets by calling this interface

#### Request

* Function：Get a list of outstanding demand notes

* Methods：`POST`

* Interface Description：`/api/operator/getUnFinishedDemandList`

* Request Example URL

  ``` html
  POST http://localhost:8080/api/operator/getUnFinishedDemandList
  ```

##### Request Parameters

​	none

##### Request Example

Get a list of outstanding demand notes

``` html
POST http://localhost:8080/api/operator/getUnFinishedDemandList
```

#### Response

| **Name**        | **Type** | **Description**                                              |
| --------------- | -------- | ------------------------------------------------------------ |
| **code**        | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**         | String   | API error code information                                   |
| **data**        | Object   | The returned data object                                     |
| **id**          | String   | Demand order ID                                              |
| **defLabel**    | String   | Demand order Name                                            |
| **description** | String   | Demand order Description                                     |
| **createdOn**   | String   | Demand order creation time                                   |

##### Response data example

###### Responses Code 200

The request is successful and the Response body format is as follows: 

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "id": "402889f782630c0f018263173dcd000d",
            "defLabel": "materialCall",
            "description": "need material：SR10-B6",
            "createdOn": "2022-08-03 17:44:03"
        }
    ]
}
```

### Batch release 

#### Interface Introduction

By invoking this interface, the agv that is waiting for release is released based on the agvId

#### Request

- Function：After the wind task uses the waiting release block, the agv currently waiting for release is released based on the agvId

- Methods：`POST`

- Interface Description：`/api/releaseWaitPassBlock`

- Request Example URL

  ```html
  POST http://localhost:8080/api/releaseWaitPassBlock
  ```

##### Request Parameters

| *Name** | Type | Description                             | Required |
| ------- | ---- | --------------------------------------- | -------- |
| agvIds  | List | List of Agvids that need to be released | Y        |



##### Request Example

The agv that is being released is allowed

```html
POST http://localhost:8080/api/releaseWaitPassBlock

["sim_01","sim_02"]
```

#### Response

| **Name** | **Type** | **Description**                                              |
| -------- | -------- | ------------------------------------------------------------ |
| **code** | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**  | String   | API error code information                                   |
| **data** | Object   | The returned data object                                     |

##### Request example

###### Responses Code 200

When the request is successful, the response body format is as follows:

```JSON
{
	"code": 200,
	"msg": "Success",
	"data": null
}
```



## Device-related interface

### Add charger information

#### Interface introduction

Add charger information to RDS by calling this API

#### Request

* function: add charger information.

* method: `POST`.

* API description: `/ chargers/ updateChargeModels`.

* request sample URL

  ``` html
  POST http://localhost:8080/chargers/updateChargeModels
  ```

##### Request parameter

Note: none of the following parameters are required

| **Name**             | **Type** | **Description**                                              |
| -------------------- | -------- | ------------------------------------------------------------ |
| **config**           | Object   | configuration information of the charger                     |
| **chargerReports**   | Object   | status reported by the charger                               |
| **vehicleInfo**      | Object   | details of the robot parked at the charging point            |
| **turnedOn**         | Boolean  | true: start charging; false (default): stop charging         |
| **enabled**          | Boolean  | true (default): enable charger; false: disable charger       |
| **timedOut**         | Boolean  | whether the charger is in a timeout state. The default is false |
| **online**           | Boolean  | online status of the charger. Default is false               |
| **voltageToCharger** | Int      | the voltage written to the charger. The value is 10 times the actual voltage |
| **currentToCharger** | Int      | the current written to the charger, which is 10 times the actual current |
| **status**           | String   | status of the charger                                        |

**Config field description:**

| **Name**               | **Type** | **Description**                                      |
| ---------------------- | -------- | ---------------------------------------------------- |
| **name**               | String   | Name of the charger                                  |
| **host**               | String   | ip                                                   |
| **port**               | Int      | port                                                 |
| **location**           | String   | Name of the workstation corresponding to the charger |
| **timeout**            | Int      | Charging timeout                                     |
| **connectTimeout**     | Int      | Connection timeout                                   |
| **printChargedStatus** | Boolean  | The way to record the information of charger         |
| **mode**               | String   | AioTcp, Tcp                                          |
| **maxVoltage**         | Int      | Safety voltage                                       |
| **maxCurrent**         | Int      | Safety current                                       |

**ChargerReports field description:**

| **Name**                | **Type** | **Description**                                              |
| ----------------------- | -------- | ------------------------------------------------------------ |
| **voltage**             | String   | The voltage read from the charger                            |
| **current**             | String   | Current read from charger                                    |
| **hdError**             | Boolean  | Hardware failure; false: normal; true: hardware failure      |
| **temperature**         | Boolean  | Whether the battery temperature is abnormal; false: normal; true: charger temperature protection |
| **inputVoltageStatus**  | Boolean  | Input voltage status; false: input voltage is normal; true: input voltage error, charger stops working |
| **armOut**              | Boolean  | Arm status; false: retraction; true: protruding              |
| **communicationStatus** | Boolean  | Communication status; false: communication is normal; true: communication reception timeout |
| **relayStatus**         | Boolean  | Relay status; false: closed; true: off                       |
| **forceBreak**          | Boolean  | Force disconnect flag; false: off; true: on                  |
| **charging**            | Boolean  | Working status; false (default): off; true: charging         |

**VehicleInfo field description:**

| **Name**             | **Type** | **Description**                                              |
| -------------------- | -------- | ------------------------------------------------------------ |
| **id**               | String   | Robot name                                                   |
| **onPosition**       | Boolean  | True: robot information can be obtained at the current charger point.  false: robot information cannot be obtained at the current charger point. |
| **requestVoltage**   | Int      | The voltage read from the battery                            |
| **requestCurrent**   | Int      | The current read from the battery                            |
| **charging**         | Boolean  | Charging status of the robot; false: not charged; true: charging |
| **forkAutoFlag**     | Boolean  | False: manual mode (charger does not work); true: automatic mode (charger can work) |
| **state**            | String   | Robot state                                                  |
| **isDominating**     | Boolean  | Whether the robot is occupied by this system                 |
| **integrationLevel** | String   | Robot online status                                          |
| **transportOrder**   | String   | The last waybill executed by the robot                       |



##### Request example

Add charger information (parameters that are not passed means you don't need to pass them. If you need to add them, you can add them yourself)

```json
 [
     {
        "config": {
            "name": "aaa",
            "host": "127.0.0.1",
            "port": "8080"
        },
        "online": true,
        "chargerReports": {
            "charging": true
        },
        "vehicleInfo":{
            "requestCurrent": 220,
            "requestVoltage": 11
        },
        "status": "ERROR"
    },
    {
        "config": {
            "name": "bbb",
            "host": "127.0.0.1",
            "port": "8081"
        },
        "online": true,
        "chargerReports": {
            "charging": true
        },
        "vehicleInfo":{
            "requestCurrent": 220,
            "requestVoltage": 11
        },
        "status": "ERROR"
    }
]
```

#### Response

| **Name** | **Type** | **Description**                                              |
| -------- | -------- | ------------------------------------------------------------ |
| **code** | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**  | String   | API error code message                                       |
| **data** | Object   | Returned data object                                         |

##### Example of response data

###### Responses Code 200

When the request is successful, the response body format is as follows:

```json
{
    "code": 200,
    "msg": "Success",
    "data": null
}
```

### Query charger information

#### Interface introduction

Query charger information from RDS by calling this API

#### Request

* Function: query charger information

* Method: `GET`

* API description: `/chargers/queryChargers`

* Request sample URL

  ``` html
  GET http://localhost:8080/chargers/queryChargers
  ```

##### Request parameter

​	无

##### Request example

Get the information of the charger

``` html
GET http://localhost:8080/chargers/queryChargers
```

#### Response

| **Name** | **Type** | **Description**                                              |
| -------- | -------- | ------------------------------------------------------------ |
| **code** | int      | API error codes, see [Error Code List](#errorcodelist) for details |
| **msg**  | String   | API error code message                                       |
| **data** | Object   | Returned data object                                         |

**data field description:**

| **Name**             | **Type** | **Description**                                              |
| -------------------- | -------- | ------------------------------------------------------------ |
| **config**           | Object   | configuration information of the charger                     |
| **chargerReports**   | Object   | status reported by the charger                               |
| **vehicleInfo**      | Object   | details of the robot parked at the charging point            |
| **turnedOn**         | Boolean  | true: start charging; false (default): stop charging         |
| **enabled**          | Boolean  | true (default): enable charger; false: disable charger       |
| **timedOut**         | Boolean  | whether the charger is in a timeout state. The default is false |
| **online**           | Boolean  | online status of the charger. Default is false               |
| **voltageToCharger** | Int      | the voltage written to the charger. The value is 10 times the actual voltage |
| **currentToCharger** | Int      | the current written to the charger, which is 10 times the actual current |
| **status**           | String   | status of the charger                                        |

##### Example of response data

###### Responses Code 200

When the request is successful, the response body format is as follows:

```json
{
    "code": 200,
    "msg": "Success",
    "data": [
        {
            "config": {
                "name": "aaa",
                "host": "127.0.0.1",
                "location": null,
                "mode": null,
                "port": 8080,
                "timeout": null,
                "connectTimeout": null,
                "maxVoltage": null,
                "maxCurrent": null,
                "printChargedStatus": null
            },
            "chargerReports": {
                "voltage": null,
                "current": null,
                "hdError": null,
                "temperature": null,
                "inputVoltageStatus": null,
                "armOut": null,
                "communicationStatus": null,
                "relayStatus": null,
                "forceBreak": null,
                "charging": true
            },
            "vehicleInfo": {
                "id": null,
                "onPosition": null,
                "requestVoltage": 11,
                "requestCurrent": 220,
                "charging": null,
                "forkAutoFlag": null,
                "state": null,
                "isDominating": null,
                "integrationLevel": null,
                "transportOrder": null
            },
            "turnedOn": null,
            "enabled": null,
            "timedOut": null,
            "online": true,
            "voltageToCharger": null,
            "currentToCharger": null,
            "status": "ERROR",
            "given": null
        },
        {
            "config": {
                "name": "bbb",
                "host": "127.0.0.1",
                "location": null,
                "mode": null,
                "port": 8081,
                "timeout": null,
                "connectTimeout": null,
                "maxVoltage": null,
                "maxCurrent": null,
                "printChargedStatus": null
            },
            "chargerReports": {
                "voltage": null,
                "current": null,
                "hdError": null,
                "temperature": null,
                "inputVoltageStatus": null,
                "armOut": null,
                "communicationStatus": null,
                "relayStatus": null,
                "forceBreak": null,
                "charging": true
            },
            "vehicleInfo": {
                "id": null,
                "onPosition": null,
                "requestVoltage": 11,
                "requestCurrent": 220,
                "charging": null,
                "forkAutoFlag": null,
                "state": null,
                "isDominating": null,
                "integrationLevel": null,
                "transportOrder": null
            },
            "turnedOn": null,
            "enabled": null,
            "timedOut": null,
            "online": true,
            "voltageToCharger": null,
            "currentToCharger": null,
            "status": "ERROR",
            "given": null
        }
    ]
}
```



## <a name='errorcodelist'>Error Code List</a>

| Error code (code) |               Message                |                        Description                        |
| :---------------: | :----------------------------------: | :-------------------------------------------------------: |
|       9000        |            global error:             |                   System business error                   |
|       9022        |         Only 0/1 Is Allowed          |                    Only 0/1 Is Allowed                    |
|       9023        |       Only 0/1/null Is Allowed       |                 Only 0/1/null Is Allowed                  |
|       9024        |           SiteId Not Exist           |                     SiteId Not Exist                      |
|       9035        |       Field \`level\` Required       | Miss stat field \`level\` or use an unsupported \`level\` |
|       9036        |       Field \`types\` Required       |   Miss stat field \`types\` or use an unsupported type    |
|       9037        | Field \`start\` and \`end\` Required |           Miss stat field \`start\` or \`end\`            |
|       9038        |       Invalid Stat Time Format       |                 Invalid Stat Time Format                  |
