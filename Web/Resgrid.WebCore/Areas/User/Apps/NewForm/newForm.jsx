import React, { Component } from "react";
import ReactDOM from 'react-dom';
import * as SurveyKo from "survey-knockout";
import * as SurveyJSCreator from "survey-creator";

import "jquery-ui/themes/base/all.css";
import "nouislider/distribute/nouislider.css";
import "select2/dist/css/select2.css";
import "bootstrap-slider/dist/css/bootstrap-slider.css";

import "jquery-bar-rating/dist/themes/css-stars.css";
import "jquery-bar-rating/dist/themes/fontawesome-stars.css";

import $ from "jquery";
import "jquery-ui/ui/widgets/datepicker.js";
import "select2/dist/js/select2.js";
import "jquery-bar-rating";

//import "icheck/skins/square/blue.css";
import "pretty-checkbox/dist/pretty-checkbox.css";

import * as widgets from "surveyjs-widgets";

class NewForm extends Component {
    surveyCreator;
    componentDidMount() {
        let options = { showEmbededSurveyTab: true };
        this.surveyCreator = new SurveyJSCreator.SurveyCreator(
            null,
            options
        );
        this.surveyCreator.saveSurveyFunc = this.saveMySurvey;
        this.surveyCreator.tabs().push({
            name: "survey-templates",
            title: "My Custom Tab",
            template: "custom-tab-survey-templates",
            action: () => {
                this.surveyCreator.makeNewViewActive("survey-templates");
            },
            data: {},
        });
        this.surveyCreator.render("surveyCreatorContainer");
    }
    render() {
        return (<div>
            <script type="text/html" id="custom-tab-survey-templates">
                {`<div id="test">TEST</div>`}
            </script>

            <div id="surveyCreatorContainer" />
        </div>);
    }
    saveMySurvey = () => {
        console.log(JSON.stringify(this.surveyCreator.text));
    };
}

export default NewForm;

ReactDOM.render(<NewForm />, document.querySelector("#new-form"));
