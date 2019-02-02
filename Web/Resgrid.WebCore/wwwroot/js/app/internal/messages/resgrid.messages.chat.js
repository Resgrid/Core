
var Twilio;
var resgrid;
(function (resgrid) {
    var message;
    (function (message_1) {
        var chat;
        (function (chat) {
            var accessManager;
            var messagingClient;
            var token;
            var groups;
            var channels;
            var twilioChannels;
            var activeChannel;
            var activeChannelPage;
            var typingMembers;
            $(document).ready(function () {
                $.get(resgrid.absoluteBaseUrl + "/User/Chat/GetResponderChatSettings", function (data) {
                    if (data) {
                        token = data.Token;
                        groups = data.Groups;
                        departmentId = data.Did;
                        departmentName = data.Name;
                        channels = data.Channels;
                        console.log('Channels Users should be in: ' + JSON.stringify(channels));
                        //for (var i = 0; i < response.data.Channels.length; i++) {
                        //if (response.data.Channels[i].uniqueName.indexOf('Department:') < 0){
                        //	if (groups.filter(function(e) { return e.Gid == response.data.Channels[i].uniqueName.replace('Group:',''); }).length > 0) {
                        //		channels.push(response.data.Channels[i]);
                        //	}
                        //} else {
                        //	channels.push(response.data.Channels[i]);
                        //}
                        //}
                        initTwilioMessaging();
                    }
                });
            });
            function initTwilioMessaging() {
                //accessManager = new Twilio.AccessManager(token);
                messagingClient = new Twilio.Chat.Client(token);
                console.log('Chat token: ' + token);
                messagingClient.initialize()
                    .then(function (data) {
                    console.log(JSON.stringify(data));
                    updateChannels();
                });
            }
            function updateChannels() {
                $('#chatGroups').empty();
                //channels.forEach(function (channel) {
                //	switch (channel.status) {
                //		case 'joined':
                //			addJoinedChannel(channel);
                //			break;
                //		case 'invited':
                //			//addInvitedChannel(channel);
                //			break;
                //		default:
                //			//addKnownChannel(channel);
                //			break;
                //	}
                //});
                messagingClient.getUserChannels()
                    .then(function (page) {
                    console.log(JSON.stringify(page));
                    channels = page.items.sort(function (a, b) {
                        return a.friendlyName > b.friendlyName;
                    });
                    twilioChannels = channels;
                    channels.forEach(function (channel) {
                        switch (channel.status) {
                            case 'joined':
                                addJoinedChannel(channel);
                                break;
                            case 'invited':
                                //addInvitedChannel(channel);
                                break;
                            default:
                                //addKnownChannel(channel);
                                break;
                        }
                    });
                });
            }
            //<div class="left" >
            //			<div class="author-name" >
            //		Monica Jackson < small class="chat-date" >
            //			10:02 am
            //				< /small>
            //				< /div>
            //				< div class="chat-message active" >
            //					Lorem Ipsum is simply dummy text input.
            //			</div>
            //						< /div>
            function addJoinedChannel(channel) {
                var $el = $('<div/>')
                    .attr('data-sid', channel.Sid)
                    .on('click', function () {
                    setActiveChannel(channel);
                });
                var $title = $('<div class="author-name"/>')
                    .text(channel.Name)
                    .appendTo($el);
                var $count = $('<span class="messages-count"/>')
                    .appendTo($el);
                /*
                channel.getUnreadMessagesCount().then(count => {
                    if (count > 0) {
                        $el.addClass('new-messages');
                        $count.text(count);
                    }
                });
                */
                //var $leave = $('<div class="remove-button glyphicon glyphicon-remove"/>')
                //	.on('click', function (e) {
                //		e.stopPropagation();
                //		channel.leave();
                //	}).appendTo($el);
                $('#chatGroups').append($el);
            }
            function setActiveChannel(channel) {
                if (activeChannel) {
                    activeChannel.removeListener('messageAdded', addMessage);
                    //activeChannel.removeListener('messageRemoved', removeMessage);
                    //activeChannel.removeListener('messageUpdated', updateMessage);
                    activeChannel.removeListener('updated', updateActiveChannel);
                    activeChannel.removeListener('memberUpdated', updateMember);
                }
                activeChannel = channel;
                $('#channel-title').text(channel.friendlyName);
                $('#channel-messages ul').empty();
                $('#channel-members ul').empty();
                activeChannel.getAttributes().then(function (attributes) {
                    $('#channel-desc').text(attributes.description);
                });
                $('#send-message').off('click');
                $('#send-message').on('click', function () {
                    var body = $('#message-body-input').val();
                    channel.sendMessage(body).then(function () {
                        $('#message-body-input').val('').focus();
                        $('#channel-messages').scrollTop($('#channel-messages ul').height());
                        $('#channel-messages li.last-read').removeClass('last-read');
                    });
                });
                activeChannel.on('updated', updateActiveChannel);
                $('#no-channel').hide();
                $('#channel').show();
                if (channel.status !== 'joined') {
                    $('#channel').addClass('view-only');
                    return;
                }
                else {
                    $('#channel').removeClass('view-only');
                }
                channel.getMessages(30).then(function (page) {
                    activeChannelPage = page;
                    page.items.forEach(addMessage);
                    channel.on('messageAdded', addMessage);
                    channel.on('messageUpdated', updateMessage);
                    channel.on('messageRemoved', removeMessage);
                    var newestMessageIndex = page.items.length ? page.items[page.items.length - 1].index : 0;
                    var lastIndex = channel.lastConsumedMessageIndex;
                    if (lastIndex && lastIndex !== newestMessageIndex) {
                        var $li = $('li[data-index=' + lastIndex + ']');
                        var top = $li.position() && $li.position().top;
                        $li.addClass('last-read');
                        $('#channel-messages').scrollTop(top + $('#channel-messages').scrollTop());
                    }
                    if ($('#channel-messages ul').height() <= $('#channel-messages').height()) {
                        channel.updateLastConsumedMessageIndex(newestMessageIndex).then(updateChannels);
                    }
                    return channel.getMembers();
                }).then(function (members) {
                    updateMembers();
                    channel.on('memberJoined', updateMembers);
                    channel.on('memberLeft', updateMembers);
                    channel.on('memberUpdated', updateMember);
                    members.forEach(function (member) {
                        member.userInfo.on('updated', function () {
                            updateMember.bind(null, member);
                            updateMembers();
                        });
                    });
                });
                channel.on('typingStarted', function (member) {
                    typingMembers.add(member.userInfo.friendlyName || member.userInfo.identity);
                    updateTypingIndicator();
                });
                channel.on('typingEnded', function (member) {
                    typingMembers["delete"](member.userInfo.friendlyName || member.userInfo.identity);
                    updateTypingIndicator();
                });
                $('#message-body-input').focus();
            }
            function removeMessage(message) {
                //$('#channel-messages li[data-index=' + message.index + ']').remove();
            }
            function updateMessage(message) {
                //var $el = $('#channel-messages li[data-index=' + message.index + ']');
                //$el.empty();
                //createMessage(message, $el);
            }
            function addMessage(message) {
                var $messages = $('#chats');
                var initHeight = $('#chats').height();
                var $el = $('<div/>').attr('data-index', message.index);
                createMessage(message, $el);
                $('#chats').append($el);
                if (initHeight - 50 < $messages.scrollTop() + $messages.height()) {
                    $messages.scrollTop($('#chats').height());
                }
                if ($('#chats').height() <= $messages.height() &&
                    message.index > message.channel.lastConsumedMessageIndex) {
                    message.channel.updateLastConsumedMessageIndex(message.index);
                }
            }
            function updateActiveChannel() {
                $('#chatTitle').text(activeChannel.friendlyName);
                //$('#channel-desc').text(activeChannel.attributes.description);
            }
            function updateMembers() {
                $('#channel-members ul').empty();
                //activeChannel.getMembers()
                //	.then(members => members
                //		.sort(function (a, b) { return a.identity > b.identity; })
                //		.sort(function (a, b) { return a.userInfo.online < b.userInfo.online; })
                //		.forEach(addMember));
            }
            function addMember(member) {
                //var $el = $('<li/>')
                //	.attr('data-identity', member.userInfo.identity);
                //var $img = $('<img/>')
                //	.attr('src', 'http://gravatar.com/avatar/' + MD5(member.identity.toLowerCase()) + '?s=20&d=mm&r=g')
                //	.appendTo($el);
                //let hasReachability = (member.userInfo.online !== null) && (typeof member.userInfo.online !== 'undefined');
                //var $span = $('<span/>')
                //	.text(member.userInfo.friendlyName || member.userInfo.identity)
                //	.addClass(hasReachability ? (member.userInfo.online ? 'member-online' : 'member-offline') : '')
                //	.appendTo($el);
                //var $remove = $('<div class="remove-button glyphicon glyphicon-remove"/>')
                //	.on('click', member.remove.bind(member))
                //	.appendTo($el);
                //updateMember(member);
                //$('#channel-members ul').append($el);
            }
            function updateTypingIndicator() {
                var message = 'Typing: ';
                var names = Array.from(typingMembers).slice(0, 3);
                if (typingMembers.size) {
                    message += names.join(', ');
                }
                if (typingMembers.size > 3) {
                    message += ', and ' + (typingMembers.size - 3) + 'more';
                }
                if (typingMembers.size) {
                    message += '...';
                }
                else {
                    message = '';
                }
                $('#typing-indicator span').text(message);
            }
            function updateMember(member) {
                //if (member.identity === decodeURIComponent(client.identity)) { return; }
                //var $lastRead = $('#channel-messages p.members-read img[data-identity="' + member.identity + '"]');
                //if (!$lastRead.length) {
                //	$lastRead = $('<img/>')
                //		.attr('src', 'http://gravatar.com/avatar/' + MD5(member.identity) + '?s=20&d=mm&r=g')
                //		.attr('title', member.userInfo.friendlyName || member.userInfo.identity)
                //		.attr('data-identity', member.identity);
                //}
                //var lastIndex = member.lastConsumedMessageIndex;
                //if (lastIndex) {
                //	$('#channel-messages li[data-index=' + lastIndex + '] p.members-read').append($lastRead);
                //}
            }
            function createMessage(message, $el) {
                //var $remove = $('<div class="remove-button glyphicon glyphicon-remove"/>')
                //	.on('click', function (e) {
                //		e.preventDefault();
                //		message.remove();
                //	}).appendTo($el);
                //var $edit = $('<div class="remove-button glyphicon glyphicon-edit"/>')
                //	.on('click', function (e) {
                //		e.preventDefault();
                //		$('.body', $el).hide();
                //		$('.edit-body', $el).show();
                //		$('button', $el).show();
                //		$el.addClass('editing');
                //	}).appendTo($el);
                //var $img = $('<img/>')
                //	.attr('src', 'http://gravatar.com/avatar/' + MD5(message.author) + '?s=30&d=mm&r=g')
                //	.appendTo($el);
                var $author = $('<p class="author-name"/>')
                    .text(message.author)
                    .appendTo($el);
                var time = message.timestamp;
                var minutes = time.getMinutes();
                var ampm = Math.floor(time.getHours() / 12) ? 'PM' : 'AM';
                if (minutes < 10) {
                    minutes = '0' + minutes;
                }
                var $timestamp = $('<small class="timestamp"/>')
                    .text('(' + (time.getHours() % 12) + ':' + minutes + ' ' + ampm + ')')
                    .appendTo($author);
                //if (message.lastUpdatedBy) {
                //	time = message.dateUpdated;
                //	minutes = time.getMinutes();
                //	ampm = Math.floor(time.getHours() / 12) ? 'PM' : 'AM';
                //	if (minutes < 10) { minutes = '0' + minutes; }
                //	$('<span class="timestamp"/>')
                //		.text('(Edited by ' + message.lastUpdatedBy + ' at ' +
                //		(time.getHours() % 12) + ':' + minutes + ' ' + ampm + ')')
                //		.appendTo($author)
                //}
                var $body = $('<div class="chat-message active"/>')
                    .text(message.body)
                    .appendTo($el);
                //var $editBody = $('<textarea class="edit-body"/>')
                //	.text(message.body)
                //	.appendTo($el);
                //var $cancel = $('<button class="cancel-edit"/>')
                //	.text('Cancel')
                //	.on('click', function (e) {
                //		e.preventDefault();
                //		$('.edit-body', $el).hide();
                //		$('button', $el).hide();
                //		$('.body', $el).show();
                //		$el.removeClass('editing');
                //	}).appendTo($el);
                //var $edit = $('<button class="red-button"/>')
                //	.text('Make Change')
                //	.on('click', function (e) {
                //		message.updateBody($editBody.val());
                //	}).appendTo($el);
                var $lastRead = $('<p class="last-read"/>')
                    .text('New messages')
                    .appendTo($el);
                var $membersRead = $('<p class="members-read"/>')
                    .appendTo($el);
            }
        })(chat = message_1.chat || (message_1.chat = {}));
    })(message = resgrid.message || (resgrid.message = {}));
})(resgrid || (resgrid = {}));
