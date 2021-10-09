import React, { Component } from 'react';
import ReactDOM from 'react-dom';
import MaterialTable from 'material-table';

class PersonnelList extends Component {
    constructor() {
        super();
        this.state = {
        };
    }

    render() {
        return (
            <MaterialTable
                title=""
                columns={[
                    { title: 'Name', field: 'Name' },
                    { title: 'Group', field: 'Group' },
                    { title: 'Roles', field: 'Roles' },
                    { title: 'State', field: 'State' }
                ]}
                data={query =>
                    new Promise((resolve, reject) => {
                        let url = resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelListPaged?';
                        url += 'perPage=' + query.pageSize;
                        url += '&page=' + (query.page + 1);

                        fetch(url)
                            .then(response => response.json())
                            .then(result => {
                                resolve({
                                    data: result.Data,
                                    page: result.Page - 1,
                                    totalCount: result.Total
                                });
                            });
                    })
                }
                options={{
                    sorting: true
                }}
            />
        );
    }
}

ReactDOM.render(<PersonnelList />, document.querySelector("#personnel-list"));
